using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using KeyStats.Helpers;
using KeyStats.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace KeyStats.Services;

/// <summary>
/// Collects everyday usage insights that the plain counters cannot express:
/// typing accuracy (backspace ratio), shortcut usage, focus (app switching and
/// longest uninterrupted stretch), working-hour distribution, and how long the
/// current uninterrupted session has been going (drives the break reminder).
///
/// Stored in insights.json, completely separate from DailyStats/history/sync.
/// </summary>
public sealed class DailyInsightsService : IDisposable
{
    private static DailyInsightsService? _instance;
    public static DailyInsightsService Instance => _instance ??= new DailyInsightsService();

    private const int TickIntervalMs = 1000;
    private const int SaveIntervalTicks = 30;
    private const int MaxStoredDays = 400;

    /// <summary>A gap longer than this ends the continuous-use streak.</summary>
    private const double ContinuousIdleResetSeconds = 180.0;

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly Dictionary<string, DailyInsights> _days = new(StringComparer.Ordinal);

    private DailyInsights _today;
    private Timer? _tickTimer;
    private bool _isRunning;
    private bool _isDisposed;

    private DateTime _lastInputUtc;
    private string _currentAppName = string.Empty;
    private string _currentDisplayName = string.Empty;
    private double _currentAppStreakSeconds;
    private int _ticksSinceSave;

    /// <summary>Set by GameSessionService: game input never counts as "work activity".</summary>
    private volatile bool _gameSessionActive;

    private DailyInsightsService()
    {
        _filePath = Path.Combine(AppPaths.GetDataFolder(), "insights.json");
        Load();
        _today = GetOrCreateTodayLocked();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed)
            {
                return;
            }

            _isRunning = true;
        }

        var monitor = InputMonitorService.Instance;
        monitor.KeyPressed += OnKeyPressed;
        monitor.LeftMouseClicked += OnLeftClick;
        monitor.RightMouseClicked += OnRightClick;
        monitor.MiddleMouseClicked += OnMiddleClick;

        _tickTimer = new Timer(_ => SafeTick(), null, TickIntervalMs, TickIntervalMs);
        Debug.WriteLine("Daily insights tracking started");
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        var monitor = InputMonitorService.Instance;
        monitor.KeyPressed -= OnKeyPressed;
        monitor.LeftMouseClicked -= OnLeftClick;
        monitor.RightMouseClicked -= OnRightClick;
        monitor.MiddleMouseClicked -= OnMiddleClick;

        _tickTimer?.Dispose();
        _tickTimer = null;

        lock (_lock)
        {
            SaveLocked();
        }

        Debug.WriteLine("Daily insights tracking stopped");
    }

    public void SetGameSessionActive(bool active)
    {
        _gameSessionActive = active;
    }

    #region Input handlers

    private void OnKeyPressed(string keyName, string appName, string displayName)
    {
        if (_gameSessionActive)
        {
            return;
        }

        lock (_lock)
        {
            EnsureCurrentDayLocked();
            var hour = DateTime.Now.Hour;

            _today.TotalKeys++;
            _today.HourlyKeys[hour]++;

            if (string.Equals(keyName, "Backspace", StringComparison.OrdinalIgnoreCase))
            {
                _today.BackspaceKeys++;
            }
            else if (IsModifierKey(keyName))
            {
                _today.ModifierKeys++;
            }

            _lastInputUtc = DateTime.UtcNow;
        }
    }

    private void OnLeftClick(string appName, string displayName) => RecordClick();

    private void OnRightClick(string appName, string displayName) => RecordClick();

    private void OnMiddleClick(string appName, string displayName) => RecordClick();

    private void RecordClick()
    {
        if (_gameSessionActive)
        {
            return;
        }

        lock (_lock)
        {
            EnsureCurrentDayLocked();
            _today.TotalClicks++;
            _today.HourlyClicks[DateTime.Now.Hour]++;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    private static bool IsModifierKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        return keyName.IndexOf("ctrl", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyName.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyName.IndexOf("alt", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyName.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region Tick

    private void SafeTick()
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Insights tick failed: {ex.Message}");
        }
    }

    private void TickCore()
    {
        var nowUtc = DateTime.UtcNow;

        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            EnsureCurrentDayLocked();
            TrackForegroundApp();
            TrackContinuousUse(nowUtc);
            EvaluateBreakReminder();

            _ticksSinceSave++;
            if (_ticksSinceSave >= SaveIntervalTicks)
            {
                _ticksSinceSave = 0;
                SaveLocked();
            }
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void TrackForegroundApp()
    {
        var info = ActiveWindowManager.GetActiveAppInfo();
        var appName = info.AppName;

        if (string.IsNullOrWhiteSpace(appName) ||
            string.Equals(appName, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(appName, _currentAppName, StringComparison.OrdinalIgnoreCase))
        {
            _currentAppName = appName;
            _currentDisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? appName : info.DisplayName;
            _currentAppStreakSeconds = 0;

            if (_today.Apps.Count > 0 || _today.ActiveSeconds > 0)
            {
                _today.AppSwitches++;
            }
        }

        if (!_today.Apps.TryGetValue(_currentAppName, out var entry))
        {
            entry = new AppFocusEntry
            {
                AppName = _currentAppName,
                DisplayName = _currentDisplayName
            };
            _today.Apps[_currentAppName] = entry;
        }
        else if (!string.IsNullOrWhiteSpace(_currentDisplayName) &&
                 !string.Equals(entry.DisplayName, _currentDisplayName, StringComparison.Ordinal))
        {
            entry.DisplayName = _currentDisplayName;
        }

        entry.Seconds += 1;
        _currentAppStreakSeconds += 1;
        if (_currentAppStreakSeconds > entry.LongestStreakSeconds)
        {
            entry.LongestStreakSeconds = _currentAppStreakSeconds;
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void TrackContinuousUse(DateTime nowUtc)
    {
        if (_lastInputUtc == default)
        {
            return;
        }

        var idleSeconds = (nowUtc - _lastInputUtc).TotalSeconds;

        if (idleSeconds <= ContinuousIdleResetSeconds)
        {
            _today.CurrentStreakSeconds += 1;
            _today.ActiveSeconds += 1;
            if (_today.CurrentStreakSeconds > _today.LongestStreakSeconds)
            {
                _today.LongestStreakSeconds = _today.CurrentStreakSeconds;
            }

            return;
        }

        if (_today.CurrentStreakSeconds > 0)
        {
            _today.CurrentStreakSeconds = 0;
            _today.BreakReminderSent = false;
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void EvaluateBreakReminder()
    {
        var settings = StatsManager.Instance.Settings;
        if (!settings.WellnessReminderEnabled || _today.BreakReminderSent)
        {
            return;
        }

        var thresholdSeconds = Math.Max(10, settings.WellnessReminderMinutes) * 60.0;
        if (_today.CurrentStreakSeconds < thresholdSeconds)
        {
            return;
        }

        _today.BreakReminderSent = true;
        var minutes = (int)Math.Round(_today.CurrentStreakSeconds / 60.0);
        SendBreakReminder(minutes);
    }

    private static void SendBreakReminder(int minutes)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            var show = new Action(() =>
            {
                try
                {
                    new ToastContentBuilder()
                        .AddText(KeyStats.Properties.Strings.Insights_BreakReminderTitle)
                        .AddText(string.Format(
                            KeyStats.Properties.Strings.Insights_BreakReminderBodyFormat,
                            minutes))
                        .Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Break reminder failed: {ex.Message}");
                }
            });

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(show);
            }
            else
            {
                show();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Break reminder dispatch failed: {ex.Message}");
        }
    }

    #endregion

    #region Snapshots

    public DailyInsights GetTodaySnapshot()
    {
        lock (_lock)
        {
            EnsureCurrentDayLocked();
            return Clone(_today);
        }
    }

    public List<DailyInsights> GetRecentDays(int count)
    {
        lock (_lock)
        {
            EnsureCurrentDayLocked();
            return _days.Values
                .OrderByDescending(d => d.Date)
                .Take(Math.Max(1, count))
                .Select(Clone)
                .ToList();
        }
    }

    private static DailyInsights Clone(DailyInsights source)
    {
        return new DailyInsights
        {
            Date = source.Date,
            TotalKeys = source.TotalKeys,
            BackspaceKeys = source.BackspaceKeys,
            ModifierKeys = source.ModifierKeys,
            TotalClicks = source.TotalClicks,
            AppSwitches = source.AppSwitches,
            HourlyKeys = (int[])source.HourlyKeys.Clone(),
            HourlyClicks = (int[])source.HourlyClicks.Clone(),
            Apps = source.Apps.ToDictionary(
                kvp => kvp.Key,
                kvp => new AppFocusEntry
                {
                    AppName = kvp.Value.AppName,
                    DisplayName = kvp.Value.DisplayName,
                    Seconds = kvp.Value.Seconds,
                    LongestStreakSeconds = kvp.Value.LongestStreakSeconds
                },
                StringComparer.Ordinal),
            ActiveSeconds = source.ActiveSeconds,
            CurrentStreakSeconds = source.CurrentStreakSeconds,
            LongestStreakSeconds = source.LongestStreakSeconds,
            BreakReminderSent = source.BreakReminderSent
        };
    }

    #endregion

    #region Persistence

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void EnsureCurrentDayLocked()
    {
        var today = DateTime.Today;
        if (_today.Date == today)
        {
            return;
        }

        SaveLocked();
        _today = GetOrCreateTodayLocked();
        _currentAppName = string.Empty;
        _currentDisplayName = string.Empty;
        _currentAppStreakSeconds = 0;
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private DailyInsights GetOrCreateTodayLocked()
    {
        var key = Key(DateTime.Today);
        if (_days.TryGetValue(key, out var existing))
        {
            existing.EnsureShape();
            return existing;
        }

        var created = new DailyInsights(DateTime.Today);
        _days[key] = created;
        TrimLocked();
        return created;
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void TrimLocked()
    {
        if (_days.Count <= MaxStoredDays)
        {
            return;
        }

        var stale = _days.Values
            .OrderByDescending(d => d.Date)
            .Skip(MaxStoredDays)
            .Select(d => Key(d.Date))
            .ToList();

        foreach (var key in stale)
        {
            _days.Remove(key);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<Dictionary<string, DailyInsights>>(json);
            if (loaded == null)
            {
                return;
            }

            foreach (var pair in loaded)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                pair.Value.EnsureShape();
                _days[pair.Key] = pair.Value;
            }

            TrimLocked();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading insights: {ex.Message}");
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void SaveLocked()
    {
        try
        {
            var snapshot = new Dictionary<string, DailyInsights>(_days, StringComparer.Ordinal);
            snapshot[Key(_today.Date)] = _today;

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var tempPath = _filePath + ".tmp";
            var backupPath = _filePath + ".bak";

            using (var fs = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, backupPath);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving insights: {ex.Message}");
        }
    }

    private static string Key(DateTime date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    #endregion

    public void Dispose()
    {
        Stop();
        _isDisposed = true;
        _instance = null;
    }
}
