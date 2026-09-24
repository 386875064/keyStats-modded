using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using KeyStats.Helpers;
using KeyStats.Models;
using KeyStats.Services;
using KeyStats.Views;

namespace KeyStats.ViewModels;

public sealed class GameStatsRowItem
{
    public GameSession? Session { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public string KeysText { get; set; } = "0";
    public string ClicksText { get; set; } = "0";
    public string ApmText { get; set; } = "0";
    public string TopKeysText { get; set; } = string.Empty;
    public double KeysRatio { get; set; }
    public double ClicksRatio { get; set; }
}

public class GameStatsViewModel : ViewModelBase
{
    private const int TopKeysPerRow = 4;

    private readonly GameSessionService _service = GameSessionService.Instance;

    private int _selectedRangeIndex;
    private bool _isSessionActive;
    private bool _isGameProcessRunning;
    private string _statusText = KeyStats.Properties.Strings.GameStats_StatusIdle;
    private string _gameName = string.Empty;
    private string _elapsedText = "--:--";
    private string _apmText = "0";
    private string _keysText = "0";
    private string _clicksText = "0";
    private string _mouseDistanceText = "0";
    private string _peakApmText = "0";
    private string _peakKpmText = "0";
    private string _peakCpmText = "0";
    private string _averageApmText = "0";
    private string _lastMinuteApmText = "0";
    private string _captureStatusText = string.Empty;
    private string _channelText = string.Empty;
    private bool _captureWarning;
    private string _elevationBadgeText = string.Empty;
    private bool _isElevated;
    private string _historySummaryText = string.Empty;
    private string _topKeysText = string.Empty;
    private bool _hasHistory;
    private bool _isHistoryEmpty = true;
    private bool _overlayEnabled;
    private bool _frontmostOnly;
    private bool _mergeIntoDaily;

    public ObservableCollection<GameStatsRowItem> SessionItems { get; } = new();

    public ICommand ClearHistoryCommand { get; }
    public ICommand RestartElevatedCommand { get; }

    public GameStatsViewModel()
    {
        ClearHistoryCommand = new RelayCommand(OnClearHistory);
        RestartElevatedCommand = new RelayCommand(OnRestartElevated);

        var settings = StatsManager.Instance.Settings;
        _overlayEnabled = settings.GameOverlayEnabled;
        _frontmostOnly = settings.GameStatsFrontmostOnly;
        _mergeIntoDaily = settings.GameStatsMergeIntoDaily;
        _isElevated = ElevationHelper.IsRunningElevated();
        _elevationBadgeText = _isElevated
            ? KeyStats.Properties.Strings.GameStats_ElevatedBadge
            : KeyStats.Properties.Strings.GameStats_NotElevatedBadge;

        Refresh();
    }

    #region Live session

    public bool IsSessionActive
    {
        get => _isSessionActive;
        private set => SetProperty(ref _isSessionActive, value);
    }

    public bool IsGameProcessRunning
    {
        get => _isGameProcessRunning;
        private set => SetProperty(ref _isGameProcessRunning, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string GameName
    {
        get => _gameName;
        private set => SetProperty(ref _gameName, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string ApmText
    {
        get => _apmText;
        private set => SetProperty(ref _apmText, value);
    }

    public string KeysText
    {
        get => _keysText;
        private set => SetProperty(ref _keysText, value);
    }

    public string ClicksText
    {
        get => _clicksText;
        private set => SetProperty(ref _clicksText, value);
    }

    public string MouseDistanceText
    {
        get => _mouseDistanceText;
        private set => SetProperty(ref _mouseDistanceText, value);
    }

    public string PeakApmText
    {
        get => _peakApmText;
        private set => SetProperty(ref _peakApmText, value);
    }

    public string PeakKpmText
    {
        get => _peakKpmText;
        private set => SetProperty(ref _peakKpmText, value);
    }

    public string PeakCpmText
    {
        get => _peakCpmText;
        private set => SetProperty(ref _peakCpmText, value);
    }

    public string AverageApmText
    {
        get => _averageApmText;
        private set => SetProperty(ref _averageApmText, value);
    }

    public string LastMinuteApmText
    {
        get => _lastMinuteApmText;
        private set => SetProperty(ref _lastMinuteApmText, value);
    }

    public string CaptureStatusText
    {
        get => _captureStatusText;
        private set => SetProperty(ref _captureStatusText, value);
    }

    public bool CaptureWarning
    {
        get => _captureWarning;
        private set => SetProperty(ref _captureWarning, value);
    }

    public string ChannelText
    {
        get => _channelText;
        private set => SetProperty(ref _channelText, value);
    }

    public string ElevationBadgeText
    {
        get => _elevationBadgeText;
        private set => SetProperty(ref _elevationBadgeText, value);
    }

    public bool IsElevated
    {
        get => _isElevated;
        private set
        {
            if (SetProperty(ref _isElevated, value))
            {
                OnPropertyChanged(nameof(ShowRestartElevated));
            }
        }
    }

    public bool ShowRestartElevated => !_isElevated;

    public string TopKeysText
    {
        get => _topKeysText;
        private set => SetProperty(ref _topKeysText, value);
    }

    #endregion

    #region History

    public int SelectedRangeIndex
    {
        get => _selectedRangeIndex;
        set
        {
            if (SetProperty(ref _selectedRangeIndex, value))
            {
                RefreshHistory();
            }
        }
    }

    public string HistorySummaryText
    {
        get => _historySummaryText;
        private set => SetProperty(ref _historySummaryText, value);
    }

    public bool HasHistory
    {
        get => _hasHistory;
        private set => SetProperty(ref _hasHistory, value);
    }

    public bool IsHistoryEmpty
    {
        get => _isHistoryEmpty;
        private set => SetProperty(ref _isHistoryEmpty, value);
    }

    #endregion

    #region Options

    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set
        {
            if (!SetProperty(ref _overlayEnabled, value))
            {
                return;
            }

            var settings = StatsManager.Instance.Settings;
            settings.GameOverlayEnabled = value;
            StatsManager.Instance.SaveSettings();
            App.CurrentApp?.SetGameOverlayVisible(value);
        }
    }

    public bool FrontmostOnly
    {
        get => _frontmostOnly;
        set
        {
            if (!SetProperty(ref _frontmostOnly, value))
            {
                return;
            }

            var settings = StatsManager.Instance.Settings;
            settings.GameStatsFrontmostOnly = value;
            StatsManager.Instance.SaveSettings();
        }
    }

    /// <summary>
    /// Whether in-game input also feeds the daily totals / per-app breakdown.
    /// The match list stays independent either way.
    /// </summary>
    public bool MergeIntoDaily
    {
        get => _mergeIntoDaily;
        set
        {
            if (!SetProperty(ref _mergeIntoDaily, value))
            {
                return;
            }

            var settings = StatsManager.Instance.Settings;
            settings.GameStatsMergeIntoDaily = value;
            StatsManager.Instance.SaveSettings();
        }
    }

    #endregion

    /// <summary>Called once per second by the window's dispatcher timer.</summary>
    public void Refresh()
    {
        RefreshLive();
        RefreshHistory();
    }

    private void RefreshLive()
    {
        var snapshot = _service.GetSnapshot();

        IsSessionActive = snapshot.IsSessionActive;
        IsGameProcessRunning = snapshot.IsGameProcessRunning;
        GameName = snapshot.GameName;
        IsElevated = ElevationHelper.IsRunningElevated();

        if (snapshot.IsSessionActive)
        {
            StatusText = KeyStats.Properties.Strings.GameStats_StatusActive;
            ElapsedText = FormatDuration(snapshot.Elapsed);
            ApmText = FormatRate(snapshot.RollingApm);
            KeysText = FormatCount(snapshot.KeyPresses);
            ClicksText = FormatCount(snapshot.TotalClicks);
            MouseDistanceText = StatsManager.Instance.FormatMouseDistance(snapshot.MouseDistance);
            PeakApmText = FormatRate(snapshot.PeakApm);
            PeakKpmText = FormatRate(snapshot.PeakKpm);
            PeakCpmText = FormatRate(snapshot.PeakCpm);
            AverageApmText = FormatRate(snapshot.AverageApm);
            LastMinuteApmText = FormatRate(snapshot.LastMinuteApm);
            TopKeysText = BuildTopKeysText(snapshot.TopKeys);
        }
        else if (snapshot.IsGameProcessRunning && !string.IsNullOrWhiteSpace(snapshot.GameName))
        {
            StatusText = string.Format(
                KeyStats.Properties.Strings.GameStats_StatusWaiting,
                snapshot.GameName);
            ElapsedText = "--:--";
            ApmText = "0";
            KeysText = "0";
            ClicksText = "0";
            MouseDistanceText = StatsManager.Instance.FormatMouseDistance(0);
            PeakApmText = "0";
            PeakKpmText = "0";
            PeakCpmText = "0";
            AverageApmText = "0";
            LastMinuteApmText = "0";
            TopKeysText = string.Empty;
        }
        else
        {
            StatusText = KeyStats.Properties.Strings.GameStats_StatusIdle;
            ElapsedText = "--:--";
            ApmText = "0";
            KeysText = "0";
            ClicksText = "0";
            MouseDistanceText = StatsManager.Instance.FormatMouseDistance(0);
            PeakApmText = "0";
            PeakKpmText = "0";
            PeakCpmText = "0";
            AverageApmText = "0";
            LastMinuteApmText = "0";
            TopKeysText = string.Empty;
        }

        RefreshCaptureStatus(snapshot);
    }

    private void RefreshCaptureStatus(GameSessionSnapshot snapshot)
    {
        CaptureWarning = false;

        var channelName = snapshot.Channel == InputEventSource.RawInput
            ? KeyStats.Properties.Strings.GameStats_ChannelRawInput
            : KeyStats.Properties.Strings.GameStats_ChannelHook;
        ChannelText = string.Format(KeyStats.Properties.Strings.GameStats_ChannelFormat, channelName);

        if (snapshot.HookReinstallCount > 0)
        {
            CaptureStatusText = string.Format(
                KeyStats.Properties.Strings.GameStats_HookReinstalledFormat,
                snapshot.HookReinstallCount);
            CaptureWarning = true;
        }
        else if (snapshot.IsSessionActive && snapshot.KeyPresses + snapshot.TotalClicks == 0)
        {
            CaptureStatusText = KeyStats.Properties.Strings.GameStats_NoInputWarning;
            CaptureWarning = true;
        }
        else if (snapshot.LastInputTime.HasValue)
        {
            CaptureStatusText = string.Format(
                KeyStats.Properties.Strings.GameStats_LastInputFormat,
                snapshot.LastInputTime.Value.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        }
        else
        {
            CaptureStatusText = KeyStats.Properties.Strings.GameStats_CaptureHealthy;
        }
    }

    private void RefreshHistory()
    {
        var history = _service.GetHistorySnapshot();
        var filtered = FilterByRange(history);

        var maxKeys = Math.Max(1, filtered.Select(s => s.KeyPresses).DefaultIfEmpty(0).Max());
        var maxClicks = Math.Max(1, filtered.Select(s => s.TotalClicks).DefaultIfEmpty(0).Max());

        SessionItems.Clear();
        foreach (var session in filtered)
        {
            SessionItems.Add(new GameStatsRowItem
            {
                Session = session,
                GameName = ResolveGameName(session),
                TimeText = BuildTimeText(session),
                DurationText = FormatDuration(TimeSpan.FromSeconds(session.DurationSeconds)),
                KeysText = FormatCount(session.KeyPresses),
                ClicksText = FormatCount(session.TotalClicks),
                ApmText = FormatRate(session.AverageApm),
                TopKeysText = BuildTopKeysText(session.KeyPressCounts, TopKeysPerRow),
                KeysRatio = session.KeyPresses / (double)maxKeys,
                ClicksRatio = session.TotalClicks / (double)maxClicks
            });
        }

        var totalDuration = TimeSpan.FromSeconds(filtered.Sum(s => s.DurationSeconds));
        var totalInputs = filtered.Sum(s => s.TotalInputs);

        HistorySummaryText = filtered.Count == 0
            ? string.Empty
            : string.Format(
                KeyStats.Properties.Strings.GameStats_SummaryFormat,
                filtered.Count,
                FormatDuration(totalDuration),
                FormatCount(totalInputs));

        IsHistoryEmpty = SessionItems.Count == 0;
        HasHistory = !IsHistoryEmpty;
    }

    private List<GameSession> FilterByRange(List<GameSession> history)
    {
        var today = DateTime.Today;
        return SelectedRangeIndex switch
        {
            0 => history.Where(s => s.StartTime.Date == today).ToList(),
            1 => history.Where(s => s.StartTime.Date >= today.AddDays(-6)).ToList(),
            2 => history.Where(s => s.StartTime.Date >= today.AddDays(-29)).ToList(),
            _ => history
        };
    }

    private static string ResolveGameName(GameSession session)
    {
        var profile = GameProfileCatalog.FindById(session.GameId);
        if (profile != null)
        {
            return GameProfileCatalog.GetDisplayName(profile);
        }

        return string.IsNullOrWhiteSpace(session.GameName) ? session.GameId : session.GameName;
    }

    private static string BuildTimeText(GameSession session)
    {
        var start = session.StartTime;
        var end = session.EndTime;

        if (start.Date == DateTime.Today)
        {
            return $"{start:HH:mm}-{end:HH:mm}";
        }

        return $"{start:M-d HH:mm}-{end:HH:mm}";
    }

    private static string BuildTopKeysText(Dictionary<string, int> counts, int limit)
    {
        if (counts.Count == 0)
        {
            return string.Empty;
        }

        var top = counts
            .Where(x => x.Value > 0 && !string.IsNullOrWhiteSpace(x.Key))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => $"{x.Key}\u00D7{x.Value}");

        return string.Join("  ", top);
    }

    private static string BuildTopKeysText(List<KeyValuePair<string, int>> topKeys)
    {
        if (topKeys.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("  ", topKeys.Select(x => $"{x.Key}\u00D7{x.Value}"));
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string FormatRate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return "0";
        }

        return Math.Round(value).ToString("N0", CultureInfo.CurrentCulture);
    }

    private static string FormatCount(int value) => Math.Max(0, value).ToString("N0", CultureInfo.CurrentCulture);

    private void OnClearHistory()
    {
        var confirmed = ConfirmDialog.Show(
            KeyStats.Properties.Strings.GameStats_ClearConfirmMessage,
            KeyStats.Properties.Strings.GameStats_ClearConfirmTitle,
            KeyStats.Properties.Strings.GameStats_ClearHistory);

        if (!confirmed)
        {
            return;
        }

        _service.ClearHistory();
        RefreshHistory();
    }

    private void OnRestartElevated()
    {
        if (!ElevationHelper.TryRestartElevated())
        {
            return;
        }

        App.CurrentApp?.Shutdown();
    }
}
