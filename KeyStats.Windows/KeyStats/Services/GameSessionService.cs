using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using KeyStats.Helpers;
using KeyStats.Models;

namespace KeyStats.Services;

/// <summary>
/// Immutable view of the current game tracking state, consumed by the UI (1s polling).
/// </summary>
public sealed class GameSessionSnapshot
{
    public bool IsSessionActive { get; set; }
    public bool IsGameProcessRunning { get; set; }
    public bool IsGameForeground { get; set; }

    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public TimeSpan Elapsed { get; set; }

    public int KeyPresses { get; set; }
    public int TotalClicks { get; set; }
    public int LeftClicks { get; set; }
    public int RightClicks { get; set; }
    public int MiddleClicks { get; set; }
    public int SideBackClicks { get; set; }
    public int SideForwardClicks { get; set; }
    public double MouseDistance { get; set; }
    public double ScrollDistance { get; set; }

    /// <summary>APM observed in the most recently completed minute.</summary>
    public double LastMinuteApm { get; set; }
    public double LastMinuteKpm { get; set; }
    public double LastMinuteCpm { get; set; }

    /// <summary>Live APM: inputs recorded in the trailing 60 seconds.</summary>
    public double RollingApm { get; set; }
    public double RollingKpm { get; set; }
    public double RollingCpm { get; set; }

    public double PeakApm { get; set; }
    public double PeakKpm { get; set; }
    public double PeakCpm { get; set; }
    public double PeakKps { get; set; }
    public double AverageApm { get; set; }

    public DateTime? LastInputTime { get; set; }

    /// <summary>Most pressed keys in the current session (descending, capped).</summary>
    public List<KeyValuePair<string, int>> TopKeys { get; set; } = new();

    /// <summary>How many times the low-level hooks had to be reinstalled (anti-cheat interference indicator).</summary>
    public long HookReinstallCount { get; set; }
    public bool HookHealthy { get; set; }

    /// <summary>Capture channel currently feeding the session statistics.</summary>
    public InputEventSource Channel { get; set; }

    /// <summary>Whether the Raw Input sink registered successfully.</summary>
    public bool RawInputRegistered { get; set; }

    public int RawInputEventCount { get; set; }
    public int HookEventCount { get; set; }
}

/// <summary>
/// Detects game processes (League of Legends by default), records a dedicated
/// session for each match, and exposes live APM/KPM/CPM metrics.
/// </summary>
public sealed class GameSessionService : IDisposable
{
    private static GameSessionService? _instance;
    public static GameSessionService Instance => _instance ??= new GameSessionService();

    private const int PollIntervalMs = 1000;
    private const int ProcessMissingGracePolls = 3;
    private const double MinimumSessionSeconds = 15.0;
    private const int MaxStoredSessions = 500;
    private const double MinuteBucketSeconds = 60.0;
    private const int InProgressSavePollInterval = 10;

    private readonly object _lock = new();
    private readonly string _dataFolder;
    private readonly string _sessionsFilePath;

    private readonly List<GameSession> _history = new();

    private Timer? _pollTimer;
    private bool _isRunning;
    private bool _isDisposed;

    private GameProfile? _activeProfile;
    private GameSession? _activeSession;
    private GameSession? _lastCompletedSession;
    private int _missingPolls;
    private int _pollsSinceInProgressSave;
    private bool _isGameProcessRunning;
    private bool _isGameForeground;

    // Minute bucket used for KPM/CPM/APM peaks.
    private DateTime _bucketStartUtc;
    private int _bucketKeys;
    private int _bucketClicks;
    private int _minuteIndex;
    private double _lastMinuteApm;
    private double _lastMinuteKpm;
    private double _lastMinuteCpm;

    // 1-second sliding window used for the instantaneous KPS peak.
    private readonly Queue<DateTime> _recentKeyTicks = new();
    private readonly Queue<DateTime> _recentClickTicks = new();

    // 60-second rolling window used for the live APM readout.
    private readonly Queue<DateTime> _rollingKeyTicks = new();
    private readonly Queue<DateTime> _rollingClickTicks = new();
    private double _rollingApm;

    private DateTime? _lastInputUtc;

    // Dual-channel capture. Raw Input is the primary source for game sessions because
    // anti-cheat drivers routinely detach low-level hooks during a match; if Raw Input
    // stays silent for the whole grace window we fall back to the hook channel.
    private const double ChannelFallbackGraceSeconds = 20.0;
    private InputEventSource _activeChannel = InputEventSource.RawInput;
    private DateTime _channelFallbackDeadlineUtc;
    private int _rawInputEventCount;
    private int _hookEventCount;
    private double _pendingMouseDistance;

    /// <summary>Raised when a session starts or ends (not on every input event).</summary>
    public event Action? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isRunning;
            }
        }
    }

    public bool IsSessionActive
    {
        get
        {
            lock (_lock)
            {
                return _activeSession != null;
            }
        }
    }

    private GameSessionService()
    {
        _dataFolder = AppPaths.GetDataFolder();
        _sessionsFilePath = Path.Combine(_dataFolder, "game_sessions.json");
        LoadHistory();
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

        SetupInputMonitor();

        _pollTimer = new Timer(_ => SafePoll(), null, PollIntervalMs, PollIntervalMs);
        Debug.WriteLine("Game session tracking started");
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

        _pollTimer?.Dispose();
        _pollTimer = null;

        TeardownInputMonitor();

        lock (_lock)
        {
            if (_activeSession != null)
            {
                CompleteActiveSession(DateTime.Now, persist: true);
            }
        }

        Debug.WriteLine("Game session tracking stopped");
    }

    private void SetupInputMonitor()
    {
        var monitor = InputMonitorService.Instance;
        monitor.KeyPressed += OnHookKeyPressed;
        monitor.LeftMouseClicked += OnHookLeftClick;
        monitor.RightMouseClicked += OnHookRightClick;
        monitor.MiddleMouseClicked += OnHookMiddleClick;
        monitor.SideBackMouseClicked += OnHookSideBackClick;
        monitor.SideForwardMouseClicked += OnHookSideForwardClick;
        monitor.MouseMoved += OnHookMouseMoved;
        monitor.MouseScrolled += OnHookMouseScrolled;

        var rawInput = RawInputService.Instance;
        rawInput.KeyPressed += OnRawKeyPressed;
        rawInput.MouseButtonPressed += OnRawMouseButtonPressed;
        rawInput.MouseMoved += OnRawMouseMoved;
        rawInput.MouseWheelScrolled += OnRawMouseWheelScrolled;
        rawInput.Start();
    }

    private void TeardownInputMonitor()
    {
        var monitor = InputMonitorService.Instance;
        monitor.KeyPressed -= OnHookKeyPressed;
        monitor.LeftMouseClicked -= OnHookLeftClick;
        monitor.RightMouseClicked -= OnHookRightClick;
        monitor.MiddleMouseClicked -= OnHookMiddleClick;
        monitor.SideBackMouseClicked -= OnHookSideBackClick;
        monitor.SideForwardMouseClicked -= OnHookSideForwardClick;
        monitor.MouseMoved -= OnHookMouseMoved;
        monitor.MouseScrolled -= OnHookMouseScrolled;

        var rawInput = RawInputService.Instance;
        rawInput.KeyPressed -= OnRawKeyPressed;
        rawInput.MouseButtonPressed -= OnRawMouseButtonPressed;
        rawInput.MouseMoved -= OnRawMouseMoved;
        rawInput.MouseWheelScrolled -= OnRawMouseWheelScrolled;
        rawInput.Stop();
    }

    #region Input handlers

    // ---- Hook channel (globally installed low-level hooks) ----

    private void OnHookKeyPressed(string keyName, string appName, string displayName)
        => RecordKeyPress(keyName, InputEventSource.Hook);

    private void OnHookLeftClick(string appName, string displayName)
        => RecordClick(MouseButtonKind.Left, InputEventSource.Hook);

    private void OnHookRightClick(string appName, string displayName)
        => RecordClick(MouseButtonKind.Right, InputEventSource.Hook);

    private void OnHookMiddleClick(string appName, string displayName)
        => RecordClick(MouseButtonKind.Middle, InputEventSource.Hook);

    private void OnHookSideBackClick(string appName, string displayName)
        => RecordClick(MouseButtonKind.SideBack, InputEventSource.Hook);

    private void OnHookSideForwardClick(string appName, string displayName)
        => RecordClick(MouseButtonKind.SideForward, InputEventSource.Hook);

    private void OnHookMouseMoved(double distance)
        => RecordMouseMove(distance, InputEventSource.Hook);

    private void OnHookMouseScrolled(double distance, string appName, string displayName)
        => RecordScroll(distance, InputEventSource.Hook);

    // ---- Raw Input channel (RIDEV_INPUTSINK; survives hook detaching) ----

    private void OnRawKeyPressed(string keyName)
        => RecordKeyPress(keyName, InputEventSource.RawInput);

    private void OnRawMouseButtonPressed(MouseButtonKind kind)
        => RecordClick(kind, InputEventSource.RawInput);

    private void OnRawMouseMoved(double distance)
        => RecordMouseMove(distance, InputEventSource.RawInput);

    private void OnRawMouseWheelScrolled(int delta)
        => RecordScroll(Math.Abs(delta) / 120.0, InputEventSource.RawInput);

    // ---- Shared recording ----

    private void RecordKeyPress(string keyName, InputEventSource source)
    {
        bool accepted;
        lock (_lock)
        {
            accepted = ShouldCountInput(source);
            if (accepted)
            {
                var now = DateTime.UtcNow;
                RollMinuteBucketIfNeeded(now);

                var session = _activeSession!;
                session.KeyPresses++;
                if (!string.IsNullOrEmpty(keyName))
                {
                    if (!session.KeyPressCounts.ContainsKey(keyName))
                    {
                        session.KeyPressCounts[keyName] = 0;
                    }
                    session.KeyPressCounts[keyName]++;
                }

                _bucketKeys++;
                _lastInputUtc = now;
                _recentKeyTicks.Enqueue(now);
                _rollingKeyTicks.Enqueue(now);
            }
        }

        if (accepted)
        {
            ForwardKeyPressToDailyTotals(keyName);
        }
    }

    private void RecordClick(MouseButtonKind kind, InputEventSource source)
    {
        bool accepted;
        lock (_lock)
        {
            accepted = ShouldCountInput(source);
            if (accepted)
            {
                var now = DateTime.UtcNow;
                RollMinuteBucketIfNeeded(now);

                var session = _activeSession!;
                switch (kind)
                {
                    case MouseButtonKind.Left:
                        session.LeftClicks++;
                        break;
                    case MouseButtonKind.Right:
                        session.RightClicks++;
                        break;
                    case MouseButtonKind.Middle:
                        session.MiddleClicks++;
                        break;
                    case MouseButtonKind.SideBack:
                        session.SideBackClicks++;
                        break;
                    case MouseButtonKind.SideForward:
                        session.SideForwardClicks++;
                        break;
                }

                _bucketClicks++;
                _lastInputUtc = now;
                _recentClickTicks.Enqueue(now);
                _rollingClickTicks.Enqueue(now);
            }
        }

        if (accepted)
        {
            ForwardClickToDailyTotals(kind);
        }
    }

    private void RecordMouseMove(double distance, InputEventSource source)
    {
        if (distance <= 0)
        {
            return;
        }

        lock (_lock)
        {
            if (!ShouldCountInput(source))
            {
                return;
            }

            // Raw Input reports movement at the device polling rate (up to 1000/s);
            // accumulate here and commit once per poll tick to keep the hot path cheap.
            _pendingMouseDistance += distance;
        }
    }

    private void RecordScroll(double distance, InputEventSource source)
    {
        if (distance <= 0)
        {
            return;
        }

        bool accepted;
        lock (_lock)
        {
            accepted = ShouldCountInput(source);
            if (accepted)
            {
                _activeSession!.ScrollDistance += Math.Abs(distance);
            }
        }

        if (accepted)
        {
            ForwardScrollToDailyTotals(distance);
        }
    }

    // ---- Daily totals bridge (game input is also counted in the daily statistics) ----

    private static bool ShouldMergeIntoDailyTotals =>
        StatsManager.Instance.Settings.GameStatsMergeIntoDaily;

    private static void ForwardKeyPressToDailyTotals(string keyName)
    {
        if (!ShouldMergeIntoDailyTotals)
        {
            return;
        }

        var app = ActiveWindowManager.GetActiveAppInfo();
        StatsManager.Instance.ApplyGameKeyPress(keyName, app.AppName, app.DisplayName);
    }

    private static void ForwardClickToDailyTotals(MouseButtonKind kind)
    {
        if (!ShouldMergeIntoDailyTotals)
        {
            return;
        }

        var app = ActiveWindowManager.GetActiveAppInfo();
        StatsManager.Instance.ApplyGameClick(kind, app.AppName, app.DisplayName);
    }

    private static void ForwardScrollToDailyTotals(double distance)
    {
        if (!ShouldMergeIntoDailyTotals)
        {
            return;
        }

        var app = ActiveWindowManager.GetActiveAppInfo();
        StatsManager.Instance.ApplyGameScroll(distance, app.AppName, app.DisplayName);
    }

    private static void ForwardMouseMoveToDailyTotals(double distance)
    {
        if (!ShouldMergeIntoDailyTotals)
        {
            return;
        }

        StatsManager.Instance.ApplyGameMouseMove(distance);
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private bool ShouldCountInput(InputEventSource source)
    {
        if (_activeSession == null)
        {
            return false;
        }

        if (source == InputEventSource.Hook)
        {
            _hookEventCount++;
        }
        else
        {
            _rawInputEventCount++;
        }

        if (source != _activeChannel)
        {
            return false;
        }

        var settings = StatsManager.Instance.Settings;
        if (settings.GameStatsFrontmostOnly && !_isGameForeground)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Commits accumulated mouse movement to the session.
    /// Returns the committed distance so the caller can forward it (outside the lock).
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private double CommitPendingMouseDistance()
    {
        if (_activeSession == null || _pendingMouseDistance <= 0)
        {
            _pendingMouseDistance = 0;
            return 0;
        }

        var distance = _pendingMouseDistance;
        _activeSession.MouseDistance += distance;
        _pendingMouseDistance = 0;
        return distance;
    }

    /// <summary>
    /// Falls back from Raw Input to the hook channel when Raw Input never delivered a
    /// single event during the grace window (registered, but the OS or a game driver
    /// swallowed the events).
    /// </summary>
    private void UpdateActiveChannel(DateTime nowUtc)
    {
        if (_activeSession == null)
        {
            return;
        }

        if (_activeChannel == InputEventSource.RawInput &&
            _rawInputEventCount == 0 &&
            nowUtc >= _channelFallbackDeadlineUtc)
        {
            _activeChannel = InputEventSource.Hook;
            Debug.WriteLine("Game session: Raw Input delivered nothing, switched to the hook channel");
        }
    }

    private void RollMinuteBucketIfNeeded(DateTime nowUtc)
    {
        if (_bucketStartUtc == default)
        {
            _bucketStartUtc = nowUtc;
            _bucketKeys = 0;
            _bucketClicks = 0;
            return;
        }

        var elapsed = (nowUtc - _bucketStartUtc).TotalSeconds;
        if (elapsed < MinuteBucketSeconds)
        {
            return;
        }

        // Only treat the bucket as a valid "one minute" sample; a long idle gap
        // would otherwise report a misleadingly tiny per-minute figure.
        if (elapsed < MinuteBucketSeconds * 2 && _activeSession != null)
        {
            var kpm = _bucketKeys;
            var cpm = _bucketClicks;
            var apm = kpm + cpm;

            _lastMinuteKpm = kpm;
            _lastMinuteCpm = cpm;
            _lastMinuteApm = apm;

            if (kpm > _activeSession.PeakKpm) _activeSession.PeakKpm = kpm;
            if (cpm > _activeSession.PeakCpm) _activeSession.PeakCpm = cpm;
            if (apm > _activeSession.PeakApm) _activeSession.PeakApm = apm;
        }
        else
        {
            _lastMinuteKpm = 0;
            _lastMinuteCpm = 0;
            _lastMinuteApm = 0;
        }

        // Record the bucket for the per-minute timeline before resetting it.
        if (_activeSession != null)
        {
            _minuteIndex++;
            _activeSession.MinuteTimeline.Add(new SessionMinuteSample
            {
                Minute = _minuteIndex,
                Keys = _bucketKeys,
                Clicks = _bucketClicks,
                Seconds = elapsed
            });
        }

        _bucketStartUtc = nowUtc;
        _bucketKeys = 0;
        _bucketClicks = 0;
    }

    private void TrimSecondWindow(DateTime nowUtc)
    {
        var cutoff = nowUtc.AddSeconds(-1.0);
        while (_recentKeyTicks.Count > 0 && _recentKeyTicks.Peek() <= cutoff)
        {
            _recentKeyTicks.Dequeue();
        }

        while (_recentClickTicks.Count > 0 && _recentClickTicks.Peek() <= cutoff)
        {
            _recentClickTicks.Dequeue();
        }

        var rollingCutoff = nowUtc.AddSeconds(-60.0);
        while (_rollingKeyTicks.Count > 0 && _rollingKeyTicks.Peek() <= rollingCutoff)
        {
            _rollingKeyTicks.Dequeue();
        }

        while (_rollingClickTicks.Count > 0 && _rollingClickTicks.Peek() <= rollingCutoff)
        {
            _rollingClickTicks.Dequeue();
        }

        _rollingApm = _rollingKeyTicks.Count + _rollingClickTicks.Count;

        if (_activeSession != null)
        {
            var instantaneous = _recentKeyTicks.Count + _recentClickTicks.Count;
            if (instantaneous > _activeSession.PeakKps)
            {
                _activeSession.PeakKps = instantaneous;
            }
        }
    }

    #endregion

    #region Process polling

    private void SafePoll()
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Game session poll failed: {ex.Message}");
        }
    }

    private void Poll()
    {
        var settings = StatsManager.Instance.Settings;
        if (!settings.GameStatsEnabled)
        {
            lock (_lock)
            {
                _isGameProcessRunning = false;
                _isGameForeground = false;
                if (_activeSession != null)
                {
                    CompleteActiveSession(DateTime.Now, persist: true);
                }
            }

            return;
        }

        string? sessionProcessName = null;
        GameProfile? sessionProfile = null;
        var companionRunning = false;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var profile = GameProfileCatalog.FindBySessionProcess(name);
                if (profile != null)
                {
                    if (sessionProfile == null)
                    {
                        sessionProfile = profile;
                        sessionProcessName = name;
                    }

                    continue;
                }

                if (GameProfileCatalog.FindByAnyProcess(name) != null)
                {
                    companionRunning = true;
                }
            }
            catch
            {
                // Process exited or access denied — ignore this entry.
            }
            finally
            {
                process.Dispose();
            }
        }

        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var foregroundMatchesGame = false;
        var foregroundName = string.Empty;

        if (sessionProfile != null || companionRunning)
        {
            foregroundName = ActiveWindowManager.GetActiveProcessName();
            foregroundMatchesGame = sessionProfile != null && sessionProfile.MatchesAnyProcess(foregroundName);
        }

        var sessionStarted = false;
        var sessionEnded = false;
        double committedMouseDistance;

        lock (_lock)
        {
            _isGameProcessRunning = sessionProfile != null || companionRunning;
            _isGameForeground = foregroundMatchesGame;

            TrimSecondWindow(nowUtc);
            committedMouseDistance = CommitPendingMouseDistance();
            UpdateActiveChannel(nowUtc);

            if (sessionProfile != null)
            {
                _missingPolls = 0;

                if (_activeSession == null)
                {
                    BeginSessionLocked(sessionProfile, sessionProcessName ?? string.Empty, nowLocal);
                    sessionStarted = true;
                }
                else
                {
                    _activeSession.EndTime = nowLocal;
                    _activeSession.DurationSeconds = Math.Max(0, (nowLocal - _activeSession.StartTime).TotalSeconds);
                    _pollsSinceInProgressSave++;
                    if (_pollsSinceInProgressSave >= InProgressSavePollInterval)
                    {
                        _pollsSinceInProgressSave = 0;
                        SaveHistoryLocked();
                    }
                }
            }
            else if (_activeSession != null)
            {
                _missingPolls++;
                if (_missingPolls >= ProcessMissingGracePolls)
                {
                    CompleteActiveSession(nowLocal, persist: true);
                    sessionEnded = true;
                }
            }

            if (_activeSession != null)
            {
                RollMinuteBucketIfNeeded(nowUtc);
            }
        }

        if (committedMouseDistance > 0)
        {
            ForwardMouseMoveToDailyTotals(committedMouseDistance);
        }

        if (sessionStarted || sessionEnded)
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void BeginSessionLocked(GameProfile profile, string processName, DateTime nowLocal)
    {
        _activeProfile = profile;
        _activeSession = new GameSession
        {
            GameId = profile.Id,
            GameName = GameProfileCatalog.GetDisplayName(profile),
            ProcessName = processName,
            StartTime = nowLocal,
            EndTime = nowLocal,
            Completed = false
        };

        _missingPolls = 0;
        _pollsSinceInProgressSave = 0;
        _bucketStartUtc = DateTime.UtcNow;
        _bucketKeys = 0;
        _bucketClicks = 0;
        _minuteIndex = 0;
        _lastMinuteApm = 0;
        _lastMinuteKpm = 0;
        _lastMinuteCpm = 0;
        _recentKeyTicks.Clear();
        _recentClickTicks.Clear();
        _rollingKeyTicks.Clear();
        _rollingClickTicks.Clear();
        _rollingApm = 0;
        _lastInputUtc = null;

        _activeChannel = InputEventSource.RawInput;
        _channelFallbackDeadlineUtc = DateTime.UtcNow.AddSeconds(ChannelFallbackGraceSeconds);
        _rawInputEventCount = 0;
        _hookEventCount = 0;
        _pendingMouseDistance = 0;

        // The hook channel hands the input stream over to us for the duration of the session.
        StatsManager.Instance.SetGameSessionActive(true);
        // Game input must not be counted as everyday "work activity" in the insights.
        DailyInsightsService.Instance.SetGameSessionActive(true);

        Debug.WriteLine($"Game session started: {profile.Id} ({processName})");
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void CompleteActiveSession(DateTime nowLocal, bool persist)
    {
        var session = _activeSession;
        _activeSession = null;
        _activeProfile = null;
        StatsManager.Instance.SetGameSessionActive(false);
        DailyInsightsService.Instance.SetGameSessionActive(false);

        if (session == null)
        {
            return;
        }

        // Flush the trailing partial minute so short sessions still get a timeline point.
        if ((_bucketKeys > 0 || _bucketClicks > 0) && _bucketStartUtc != default)
        {
            var bucketSeconds = Math.Max(1.0, (DateTime.UtcNow - _bucketStartUtc).TotalSeconds);
            session.MinuteTimeline.Add(new SessionMinuteSample
            {
                Minute = session.MinuteTimeline.Count + 1,
                Keys = _bucketKeys,
                Clicks = _bucketClicks,
                Seconds = bucketSeconds
            });
        }

        session.EndTime = nowLocal;
        session.DurationSeconds = Math.Max(0, (nowLocal - session.StartTime).TotalSeconds);
        session.CaptureChannel = _activeChannel.ToString();
        session.Completed = true;

        _lastMinuteApm = 0;
        _lastMinuteKpm = 0;
        _lastMinuteCpm = 0;
        _bucketStartUtc = default;
        _bucketKeys = 0;
        _bucketClicks = 0;
        _recentKeyTicks.Clear();
        _recentClickTicks.Clear();
        _rollingKeyTicks.Clear();
        _rollingClickTicks.Clear();
        _rollingApm = 0;

        if (session.DurationSeconds < MinimumSessionSeconds || !session.HasActivity)
        {
            Debug.WriteLine($"Game session discarded (too short / no input): {session.DurationSeconds:F1}s");
            return;
        }

        _lastCompletedSession = session.Clone();
        _history.Insert(0, session.Clone());
        while (_history.Count > MaxStoredSessions)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        if (persist)
        {
            SaveHistoryLocked();
        }

        Debug.WriteLine(
            $"Game session completed: {session.DurationSeconds:F0}s, keys={session.KeyPresses}, clicks={session.TotalClicks}, avgApm={session.AverageApm:F0}");
    }

    #endregion

    #region Snapshots

    public GameSessionSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var monitor = InputMonitorService.Instance;
            var session = _activeSession;

            if (session == null)
            {
                return new GameSessionSnapshot
                {
                    IsSessionActive = false,
                    IsGameProcessRunning = _isGameProcessRunning,
                    IsGameForeground = _isGameForeground,
                    GameId = _activeProfile?.Id ?? string.Empty,
                    GameName = _activeProfile != null
                        ? GameProfileCatalog.GetDisplayName(_activeProfile)
                        : string.Empty,
                    HookReinstallCount = monitor.HookReinstallCount,
                    HookHealthy = monitor.IsMonitoring,
                    Channel = _activeChannel,
                    RawInputRegistered = RawInputService.Instance.IsRegistered
                };
            }

            var elapsed = DateTime.Now - session.StartTime;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return new GameSessionSnapshot
            {
                IsSessionActive = true,
                IsGameProcessRunning = _isGameProcessRunning,
                IsGameForeground = _isGameForeground,
                GameId = session.GameId,
                GameName = session.GameName,
                ProcessName = session.ProcessName,
                StartTime = session.StartTime,
                Elapsed = elapsed,
                KeyPresses = session.KeyPresses,
                TotalClicks = session.TotalClicks,
                LeftClicks = session.LeftClicks,
                RightClicks = session.RightClicks,
                MiddleClicks = session.MiddleClicks,
                SideBackClicks = session.SideBackClicks,
                SideForwardClicks = session.SideForwardClicks,
                MouseDistance = session.MouseDistance,
                ScrollDistance = session.ScrollDistance,
                LastMinuteApm = _lastMinuteApm,
                LastMinuteKpm = _lastMinuteKpm,
                LastMinuteCpm = _lastMinuteCpm,
                RollingApm = _rollingApm,
                RollingKpm = _rollingKeyTicks.Count,
                RollingCpm = _rollingClickTicks.Count,
                PeakApm = session.PeakApm,
                PeakKpm = session.PeakKpm,
                PeakCpm = session.PeakCpm,
                PeakKps = session.PeakKps,
                AverageApm = session.AverageApm,
                LastInputTime = _lastInputUtc?.ToLocalTime(),
                TopKeys = BuildTopKeys(session.KeyPressCounts, 8),
                HookReinstallCount = monitor.HookReinstallCount,
                HookHealthy = monitor.IsMonitoring,
                Channel = _activeChannel,
                RawInputRegistered = RawInputService.Instance.IsRegistered,
                RawInputEventCount = _rawInputEventCount,
                HookEventCount = _hookEventCount
            };
        }
    }

    private static List<KeyValuePair<string, int>> BuildTopKeys(
        Dictionary<string, int> counts,
        int limit)
    {
        return counts
            .Where(x => x.Value > 0 && !string.IsNullOrWhiteSpace(x.Key))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public List<GameSession> GetHistorySnapshot()
    {
        lock (_lock)
        {
            return _history.Select(s => s.Clone()).ToList();
        }
    }

    public GameSession? GetLastCompletedSession()
    {
        lock (_lock)
        {
            return _lastCompletedSession?.Clone();
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
            SaveHistoryLocked();
        }

        StateChanged?.Invoke();
    }

    /// <summary>Today's session count / totals, used by the summary header.</summary>
    public (int SessionCount, TimeSpan TotalDuration, int TotalInputs) GetTodayTotals()
    {
        lock (_lock)
        {
            var today = DateTime.Today;
            var sessions = _history.Where(s => s.StartTime.Date == today).ToList();
            var duration = TimeSpan.FromSeconds(sessions.Sum(s => s.DurationSeconds));
            return (sessions.Count, duration, sessions.Sum(s => s.TotalInputs));
        }
    }

    #endregion

    #region Persistence

    private void LoadHistory()
    {
        var loaded = TryDeserialize<List<GameSession>>(_sessionsFilePath)
            ?? TryDeserialize<List<GameSession>>(_sessionsFilePath + ".bak");

        if (loaded == null)
        {
            return;
        }

        var recovered = 0;
        foreach (var session in loaded)
        {
            if (session == null)
            {
                continue;
            }

            if (!session.Completed)
            {
                // The app was closed mid-session; settle it with the last known timestamp.
                session.Completed = true;
                if (session.EndTime <= session.StartTime)
                {
                    session.EndTime = session.StartTime;
                }

                session.DurationSeconds = Math.Max(0, (session.EndTime - session.StartTime).TotalSeconds);
                recovered++;
            }

            _history.Add(session);
        }

        _history.Sort((lhs, rhs) => rhs.StartTime.CompareTo(lhs.StartTime));
        while (_history.Count > MaxStoredSessions)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        if (recovered > 0)
        {
            Debug.WriteLine($"Recovered {recovered} unfinished game session(s)");
            SaveHistoryLocked();
        }
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void SaveHistoryLocked()
    {
        var payload = new List<GameSession>(_history.Count + 1);
        payload.AddRange(_history.Select(s => s.Clone()));
        if (_activeSession != null)
        {
            payload.Insert(0, _activeSession.Clone());
        }

        WriteJsonDurable(_sessionsFilePath, payload);
    }

    private static void WriteJsonDurable<T>(string targetPath, T value)
    {
        var tempPath = targetPath + ".tmp";
        var backupPath = targetPath + ".bak";

        try
        {
            var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);

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

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, backupPath);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving game sessions: {ex.Message}");
        }
    }

    private static T? TryDeserialize<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading {path}: {ex.Message}");
            return null;
        }
    }

    #endregion

    public void Dispose()
    {
        Stop();
        _isDisposed = true;
        _instance = null;
    }
}
