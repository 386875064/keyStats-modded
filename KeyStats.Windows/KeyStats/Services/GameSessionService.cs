using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using KeyStats.Helpers;
using KeyStats.Models;

namespace KeyStats.Services;

/// <summary>
/// 对局统计：检测游戏进程启动/退出，把这一局期间的按键、点击、鼠标距离单独记一条。
/// 与每日总量无关（用累计值做差），跨午夜也不会算错。
/// </summary>
public sealed class GameSessionService : IDisposable
{
    private const int PollIntervalMs = 5000;
    private const int MaxRecords = 200;
    private const double MinimumSessionSeconds = 30;

    private static readonly string[] TrackedProcesses = { "League of Legends" };
    private const string GameId = "lol";
    private const string GameName = "英雄联盟";

    private static GameSessionService? _instance;
    public static GameSessionService Instance => _instance ??= new GameSessionService();

    private readonly object _lock = new();
    private readonly List<GameSessionRecord> _records = new();

    private Timer? _timer;
    private bool _active;
    private string _activeProcessName = string.Empty;
    private DateTimeOffset _startTime;
    private long _baseKeys;
    private long _baseClicks;
    private double _baseDistance;
    private double _baseScroll;
    private long _baseLeft, _baseRight, _baseMiddle, _baseSideBack, _baseSideForward;
    private int _lastSampleMinute;
    private long _lastSampleKeys, _lastSampleClicks;
    private int _peakKpm, _peakCpm, _peakApm;

    public event Action? SessionsChanged;

    /// <summary>最近一次进程检测时间（诊断页显示）。</summary>
    public DateTimeOffset? LastScanUtc { get; private set; }

    /// <summary>最近一次匹配到的游戏进程名，未匹配为 null。</summary>
    public string? LastMatchedProcess { get; private set; }

    /// <summary>按需枚举系统里所有含 "league" 的进程名，供诊断页排查进程名不匹配。</summary>
    public IReadOnlyList<string> FindLeagueCandidates()
    {
        var names = new List<string>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name.IndexOf("league", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        names.Add(name);
                    }
                }
                catch
                {
                    // 少数系统进程读不到名字，跳过
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private GameSessionService() { }

    public bool IsInSession
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    public string ActiveGameName => GameName;

    public DateTimeOffset? ActiveStartTime
    {
        get
        {
            lock (_lock)
            {
                return _active ? _startTime : (DateTimeOffset?)null;
            }
        }
    }

    public IReadOnlyList<GameSessionRecord> GetSessions()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    public void Start()
    {
        Load();
        _timer = new Timer(Tick, null, 3000, PollIntervalMs);
        Debug.WriteLine("Game session tracking started");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        lock (_lock)
        {
            if (_active)
            {
                EndSessionLocked();
            }
        }
    }

    private void Tick(object? state)
    {
        try
        {
            var processName = ScanForGame();

            lock (_lock)
            {
                if (processName != null && !_active)
                {
                    BeginSessionLocked(processName);
                }
                else if (processName == null && _active)
                {
                    EndSessionLocked();
                }
                else if (_active)
                {
                    SampleMinuteLocked();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Game session tick failed: {ex.Message}");
        }
    }

    private void BeginSessionLocked(string processName)
    {
        var totals = StatsManager.Instance.GetLifetimeTotals();
        var clicks = StatsManager.Instance.GetLifetimeClickBreakdown();
        var movement = StatsManager.Instance.GetLifetimeMovement();

        _active = true;
        _activeProcessName = processName;
        _startTime = DateTimeOffset.Now;
        _baseKeys = totals.KeyPresses;
        _baseClicks = totals.Clicks;
        _baseDistance = movement.MouseDistance;
        _baseScroll = movement.ScrollDistance;
        _baseLeft = clicks.Left;
        _baseRight = clicks.Right;
        _baseMiddle = clicks.Middle;
        _baseSideBack = clicks.SideBack;
        _baseSideForward = clicks.SideForward;
        _lastSampleMinute = 0;
        _lastSampleKeys = 0;
        _lastSampleClicks = 0;
        _peakKpm = 0;
        _peakCpm = 0;
        _peakApm = 0;

        Debug.WriteLine($"Game session started: {processName}");
        RaiseSessionsChanged();
    }

    private void SampleMinuteLocked()
    {
        var minute = (int)(DateTimeOffset.Now - _startTime).TotalMinutes;
        if (minute <= _lastSampleMinute)
        {
            return;
        }
        _lastSampleMinute = minute;

        var totals = StatsManager.Instance.GetLifetimeTotals();
        var keysSoFar = totals.KeyPresses - _baseKeys;
        var clicksSoFar = totals.Clicks - _baseClicks;
        var minuteKeys = (int)Math.Max(0, keysSoFar - _lastSampleKeys);
        var minuteClicks = (int)Math.Max(0, clicksSoFar - _lastSampleClicks);
        _lastSampleKeys = keysSoFar;
        _lastSampleClicks = clicksSoFar;

        _peakKpm = Math.Max(_peakKpm, minuteKeys);
        _peakCpm = Math.Max(_peakCpm, minuteClicks);
        _peakApm = Math.Max(_peakApm, minuteKeys + minuteClicks);
    }

    private void EndSessionLocked()
    {
        _active = false;

        var endTime = DateTimeOffset.Now;
        var duration = (endTime - _startTime).TotalSeconds;
        if (duration < MinimumSessionSeconds)
        {
            Debug.WriteLine($"Game session too short ({duration:F0}s), dropped");
            RaiseSessionsChanged();
            return;
        }

        // 收尾采样，保证最后一分钟的峰值不被漏掉
        SampleMinuteLocked();

        var totals = StatsManager.Instance.GetLifetimeTotals();
        var clicks = StatsManager.Instance.GetLifetimeClickBreakdown();
        var movement = StatsManager.Instance.GetLifetimeMovement();

        // 不足一分钟的短局没有任何分钟采样，按整局均值补一个峰值，避免显示 0
        if (_lastSampleMinute == 0 && duration > 0)
        {
            var minutes = Math.Max(duration / 60.0, 1.0 / 60.0);
            _peakKpm = Math.Max(_peakKpm, (int)Math.Round((totals.KeyPresses - _baseKeys) / minutes));
            _peakCpm = Math.Max(_peakCpm, (int)Math.Round((totals.Clicks - _baseClicks) / minutes));
            _peakApm = Math.Max(_peakApm, _peakKpm + _peakCpm);
        }

        var record = new GameSessionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GameId = GameId,
            GameName = GameName,
            ProcessName = _activeProcessName,
            StartTime = _startTime,
            EndTime = endTime,
            DurationSeconds = duration,
            KeyPresses = Math.Max(0, totals.KeyPresses - _baseKeys),
            LeftClicks = Math.Max(0, clicks.Left - _baseLeft),
            RightClicks = Math.Max(0, clicks.Right - _baseRight),
            MiddleClicks = Math.Max(0, clicks.Middle - _baseMiddle),
            SideBackClicks = Math.Max(0, clicks.SideBack - _baseSideBack),
            SideForwardClicks = Math.Max(0, clicks.SideForward - _baseSideForward),
            MouseDistance = Math.Max(0, movement.MouseDistance - _baseDistance),
            ScrollDistance = Math.Max(0, movement.ScrollDistance - _baseScroll),
            PeakKpm = _peakKpm,
            PeakCpm = _peakCpm,
            PeakApm = _peakApm,
            CaptureChannel = InputMonitorService.Instance.CaptureChannel,
            Completed = true
        };

        _records.Insert(0, record);
        if (_records.Count > MaxRecords)
        {
            _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);
        }

        Save();
        Debug.WriteLine($"Game session ended: {record.KeyPresses} keys, {record.TotalClicks} clicks, {record.DurationText}");

        NotificationService.Instance.SendCustomNotification(
            "本局统计",
            $"{record.GameName} · {record.DurationText} · {record.KeyPresses:N0} 键 · APM {record.ApM:F0}");

        RaiseSessionsChanged();
    }

    private void RaiseSessionsChanged()
    {
        try
        {
            SessionsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionsChanged subscriber failed: {ex.Message}");
        }
    }

    private static string? FindTrackedProcess()
    {
        foreach (var name in TrackedProcesses)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0)
            {
                continue;
            }

            foreach (var process in processes)
            {
                process.Dispose();
            }
            return name;
        }

        return null;
    }

    private string? ScanForGame()
    {
        var matched = FindTrackedProcess();
        LastMatchedProcess = matched;
        LastScanUtc = DateTimeOffset.Now;
        return matched;
    }

    private string SessionsFilePath => Path.Combine(StatsManager.Instance.DataFolder, "game_sessions.json");

    private void Load()
    {
        try
        {
            var path = SessionsFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<GameSessionRecord>>(json);
            if (items == null)
            {
                return;
            }

            lock (_lock)
            {
                _records.Clear();
                _records.AddRange(items
                    .Where(item => item != null)
                    .OrderByDescending(item => item.StartTime)
                    .Take(MaxRecords));
            }
        }
        catch (Exception ex)
        {
            // 旧版本可能写了不同结构，读不了就当空，不要影响启动
            Debug.WriteLine($"Failed to load game sessions: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(SessionsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save game sessions: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _instance = null;
    }
}
