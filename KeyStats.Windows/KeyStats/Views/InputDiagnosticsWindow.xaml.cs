using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyStats.Helpers;
using KeyStats.Services;

namespace KeyStats.Views;

/// <summary>
/// 只读诊断页：显示当前采集通道、是否收到输入、归因到的前台程序与今日计数。
/// 用于判断"游戏内没记到"到底是没收到、还是根本没按键。
/// </summary>
public partial class InputDiagnosticsWindow : Window
{
    private const int SampleSeconds = 10;

    private readonly DispatcherTimer _timer;
    private readonly Queue<long> _eventSamples = new();
    private long _lastEventCounter;
    private long _eventCounter;
    private DateTime _lastCandidateScan = DateTime.MinValue;
    private IReadOnlyList<string> _candidates = Array.Empty<string>();

    public InputDiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        Refresh();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(ApplyWindowBackdrop));
    }

    private void ApplyWindowBackdrop()
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
    }

    private void Refresh()
    {
        var monitor = InputMonitorService.Instance;

        ChannelText.Text = monitor.CaptureChannel == "Raw Input" ? "Raw Input" : "钩子（Hook）";
        ChannelHintText.Text = monitor.IsRawTakeover
            ? "Raw Input 是主通道（钩子已停用）：桌面和游戏内都靠它计数，反作弊拿不掉"
            : "Raw 通道不可用，已回退到钩子；游戏内可能计数不到";

        RawReadyText.Text = monitor.IsRawInputReady ? "已就绪" : "未注册";
        MonitoringText.Text = monitor.IsMonitoring ? "运行中" : "已停止";

        var lastInput = monitor.LastInputUtc;
        if (lastInput.HasValue)
        {
            var idle = DateTime.Now - lastInput.Value;
            var summary = string.IsNullOrEmpty(monitor.LastInputSummary) ? "输入" : monitor.LastInputSummary;
            LastInputText.Text = idle.TotalSeconds < 60
                ? $"{summary} · {idle.TotalSeconds:F0} 秒前"
                : $"{summary} · {idle.TotalMinutes:F0} 分钟前";
        }
        else
        {
            LastInputText.Text = "还没有收到任何输入";
        }

        var stats = StatsManager.Instance.GetCurrentStatsSnapshot();
        TodayKeysText.Text = stats.KeyPresses.ToString("N0");
        TodayClicksText.Text = stats.TotalClicks.ToString("N0");
        TodayDistanceText.Text = StatsManager.Instance.FormatMouseDistance(stats.MouseDistance);

        // 用计数增量估算最近 10 秒的活动，比"最近一次事件"更能看出是否卡住
        var counter = stats.KeyPresses + (long)stats.TotalClicks;
        var delta = counter - _lastEventCounter;
        _lastEventCounter = counter;
        _eventCounter += delta > 0 ? delta : 0;
        _eventSamples.Enqueue(_eventCounter);

        while (_eventSamples.Count > SampleSeconds)
        {
            _eventSamples.Dequeue();
        }

        var oldest = _eventSamples.Count > 0 ? _eventSamples.Peek() : _eventCounter;
        var recent = Math.Max(0, _eventCounter - oldest);
        RecentEventsText.Text = $"{recent} 次";
        RecentEventsBar.Value = Math.Min(RecentEventsBar.Maximum, recent);

        var foreground = ActiveWindowManager.GetActiveAppInfo();
        ForegroundAppText.Text = string.IsNullOrWhiteSpace(foreground.DisplayName)
            ? "—"
            : foreground.DisplayName;

        var sessions = GameSessionService.Instance;
        if (sessions.IsInSession && sessions.ActiveStartTime.HasValue)
        {
            SessionText.Text =
                $"对局进行中：{sessions.ActiveGameName}（进程 {sessions.LastMatchedProcess}）" +
                $"· 已跑 {(DateTimeOffset.Now - sessions.ActiveStartTime.Value).TotalMinutes:F0} 分钟，退出游戏后出记录";
        }
        else
        {
            var scanned = sessions.LastScanUtc.HasValue
                ? sessions.LastScanUtc.Value.ToString("HH:mm:ss")
                : "—";

            // 候选进程扫描开销较大，最多 5 秒一次
            if ((DateTime.Now - _lastCandidateScan).TotalSeconds >= 5)
            {
                _candidates = sessions.FindLeagueCandidates();
                _lastCandidateScan = DateTime.Now;
            }

            var candidateText = _candidates.Count > 0
                ? $"系统里名称含 league 的进程：{string.Join("、", _candidates)}"
                : "系统里没有名称含 league 的进程";

            SessionText.Text = $"未检测到游戏本体（最近检查 {scanned}，每 5 秒一次）。{candidateText}";
        }
    }
}
