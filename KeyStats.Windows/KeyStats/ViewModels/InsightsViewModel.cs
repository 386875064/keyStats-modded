using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using KeyStats.Models;
using KeyStats.Services;

namespace KeyStats.ViewModels;

public sealed class HourBarItem
{
    public string HourText { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;
    public double BarHeight { get; set; }
    public bool HasValue { get; set; }
    public bool ShowLabel { get; set; }
}

public sealed class AppFocusRowItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public string LongestText { get; set; } = string.Empty;
    public double Ratio { get; set; }
}

public class InsightsViewModel : ViewModelBase
{
    private const double MaxBarHeight = 96;
    private const double MinBarHeight = 2;
    private const int TopAppCount = 8;

    private readonly DailyInsightsService _service = DailyInsightsService.Instance;

    private string _backspaceRatioText = "--";
    private string _backspaceDetailText = string.Empty;
    private string _shortcutKeysText = "0";
    private string _activeTimeText = string.Empty;
    private string _appSwitchesText = "0";
    private string _longestFocusText = "--";
    private string _currentStreakText = "--";
    private string _longestStreakText = "--";
    private bool _isEmpty = true;
    private bool _hasTopApps;
    private bool _breakReminderEnabled;
    private int _breakReminderMinutes;

    public InsightsViewModel()
    {
        var settings = StatsManager.Instance.Settings;
        _breakReminderEnabled = settings.WellnessReminderEnabled;
        _breakReminderMinutes = settings.WellnessReminderMinutes;

        Refresh();
    }

    public ObservableCollection<HourBarItem> HourBars { get; } = new();

    public ObservableCollection<AppFocusRowItem> TopApps { get; } = new();

    public List<int> BreakMinuteOptions { get; } = new() { 30, 45, 60, 90, 120 };

    #region Display properties

    public string BackspaceRatioText
    {
        get => _backspaceRatioText;
        private set => SetProperty(ref _backspaceRatioText, value);
    }

    public string BackspaceDetailText
    {
        get => _backspaceDetailText;
        private set => SetProperty(ref _backspaceDetailText, value);
    }

    public string ShortcutKeysText
    {
        get => _shortcutKeysText;
        private set => SetProperty(ref _shortcutKeysText, value);
    }

    public string ActiveTimeText
    {
        get => _activeTimeText;
        private set => SetProperty(ref _activeTimeText, value);
    }

    public string AppSwitchesText
    {
        get => _appSwitchesText;
        private set => SetProperty(ref _appSwitchesText, value);
    }

    public string LongestFocusText
    {
        get => _longestFocusText;
        private set => SetProperty(ref _longestFocusText, value);
    }

    public string CurrentStreakText
    {
        get => _currentStreakText;
        private set => SetProperty(ref _currentStreakText, value);
    }

    public string LongestStreakText
    {
        get => _longestStreakText;
        private set => SetProperty(ref _longestStreakText, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public bool HasTopApps
    {
        get => _hasTopApps;
        private set => SetProperty(ref _hasTopApps, value);
    }

    #endregion

    #region Options

    public bool BreakReminderEnabled
    {
        get => _breakReminderEnabled;
        set
        {
            if (!SetProperty(ref _breakReminderEnabled, value))
            {
                return;
            }

            var settings = StatsManager.Instance.Settings;
            settings.WellnessReminderEnabled = value;
            StatsManager.Instance.SaveSettings();
        }
    }

    public int BreakReminderMinutes
    {
        get => _breakReminderMinutes;
        set
        {
            if (!SetProperty(ref _breakReminderMinutes, value))
            {
                return;
            }

            var settings = StatsManager.Instance.Settings;
            settings.WellnessReminderMinutes = value;
            StatsManager.Instance.SaveSettings();
        }
    }

    #endregion

    public void Refresh()
    {
        var today = _service.GetTodaySnapshot();

        BackspaceRatioText = today.TotalKeys > 0
            ? (today.BackspaceRatio * 100).ToString("F1", CultureInfo.CurrentCulture) + "%"
            : "--";
        BackspaceDetailText = string.Format(
            KeyStats.Properties.Strings.Insights_BackspaceDetailFormat,
            FormatCount(today.BackspaceKeys),
            FormatCount(today.TotalKeys));
        ShortcutKeysText = FormatCount(today.ModifierKeys);
        AppSwitchesText = FormatCount(today.AppSwitches);
        ActiveTimeText = string.Format(
            KeyStats.Properties.Strings.Insights_ActiveTimeFormat,
            FormatDuration(today.ActiveSeconds));
        CurrentStreakText = FormatDuration(today.CurrentStreakSeconds);
        LongestStreakText = FormatDuration(today.LongestStreakSeconds);

        var longestFocus = today.Apps.Values
            .Where(a => a.LongestStreakSeconds > 0)
            .OrderByDescending(a => a.LongestStreakSeconds)
            .FirstOrDefault();

        LongestFocusText = longestFocus == null
            ? "--"
            : $"{FormatDuration(longestFocus.LongestStreakSeconds)} · {ResolveDisplayName(longestFocus)}";

        BuildHourBars(today);
        BuildTopApps(today);

        IsEmpty = today.TotalKeys == 0 && today.Apps.Count == 0;
    }

    private void BuildHourBars(DailyInsights today)
    {
        var counts = today.HourlyKeys ?? new int[24];
        var max = Math.Max(1, counts.DefaultIfEmpty(0).Max());
        var clicks = today.HourlyClicks ?? new int[24];

        HourBars.Clear();
        for (var hour = 0; hour < 24; hour++)
        {
            var value = hour < counts.Length ? counts[hour] : 0;
            var clickValue = hour < clicks.Length ? clicks[hour] : 0;
            var ratio = value / (double)max;

            HourBars.Add(new HourBarItem
            {
                HourText = hour.ToString(CultureInfo.CurrentCulture),
                TooltipText = string.Format(
                    KeyStats.Properties.Strings.Insights_HourTooltipFormat,
                    hour,
                    value,
                    clickValue),
                BarHeight = value <= 0 ? MinBarHeight : Math.Max(MinBarHeight, ratio * MaxBarHeight),
                HasValue = value > 0,
                ShowLabel = hour % 3 == 0
            });
        }
    }

    private void BuildTopApps(DailyInsights today)
    {
        var apps = today.Apps.Values
            .Where(a => a.Seconds >= 1)
            .OrderByDescending(a => a.Seconds)
            .Take(TopAppCount)
            .ToList();

        var maxSeconds = Math.Max(1.0, apps.Select(a => a.Seconds).DefaultIfEmpty(0).Max());

        TopApps.Clear();
        foreach (var app in apps)
        {
            TopApps.Add(new AppFocusRowItem
            {
                DisplayName = ResolveDisplayName(app),
                DurationText = FormatDuration(app.Seconds),
                LongestText = FormatDuration(app.LongestStreakSeconds),
                Ratio = app.Seconds / maxSeconds
            });
        }

        HasTopApps = TopApps.Count > 0;
    }

    private static string ResolveDisplayName(AppFocusEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.DisplayName) &&
            !string.Equals(entry.DisplayName, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return entry.DisplayName;
        }

        return string.IsNullOrWhiteSpace(entry.AppName) ? "?" : entry.AppName;
    }

    private static string FormatCount(int value) =>
        Math.Max(0, value).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
        {
            return string.Format(KeyStats.Properties.Strings.Insights_MinutesFormat, 0);
        }

        if (seconds >= 3600)
        {
            return string.Format(
                KeyStats.Properties.Strings.Insights_HoursMinutesFormat,
                (int)(seconds / 3600),
                (int)(seconds % 3600 / 60));
        }

        return string.Format(
            KeyStats.Properties.Strings.Insights_MinutesFormat,
            Math.Max(1, (int)Math.Round(seconds / 60.0)));
    }
}
