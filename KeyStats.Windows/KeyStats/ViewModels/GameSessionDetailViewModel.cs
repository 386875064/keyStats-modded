using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using KeyStats.Models;
using KeyStats.Services;

namespace KeyStats.ViewModels;

public sealed class TimelineBarItem
{
    public string ValueText { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;
    public string MinuteText { get; set; } = string.Empty;
    public double BarHeight { get; set; }
    public bool ShowMinuteLabel { get; set; }
    public bool HasValue { get; set; }
}

public class GameSessionDetailViewModel : ViewModelBase
{
    private const double MaxBarHeight = 128;
    private const double MinBarHeight = 2;

    private readonly GameSession _session;

    public GameSessionDetailViewModel(GameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        GameName = ResolveGameName(session);
        TimeText = BuildTimeText(session);
        DurationText = FormatDuration(TimeSpan.FromSeconds(session.DurationSeconds));

        KeysText = FormatCount(session.KeyPresses);
        LeftText = FormatCount(session.LeftClicks);
        RightText = FormatCount(session.RightClicks);
        MiddleText = FormatCount(session.MiddleClicks);
        SideText = FormatCount(session.SideBackClicks + session.SideForwardClicks);
        ClicksText = FormatCount(session.TotalClicks);
        MouseDistanceText = StatsManager.Instance.FormatMouseDistance(session.MouseDistance);
        ScrollText = FormatScroll(session.ScrollDistance);

        AverageApmText = FormatRate(session.AverageApm);
        PeakApmText = FormatRate(session.PeakApm);
        PeakKpmText = FormatRate(session.PeakKpm);
        PeakCpmText = FormatRate(session.PeakCpm);
        PeakKpsText = FormatRate(session.PeakKps);

        CaptureChannelText = string.IsNullOrWhiteSpace(session.CaptureChannel)
            ? string.Empty
            : string.Format(
                KeyStats.Properties.Strings.GameStats_ChannelFormat,
                session.CaptureChannel == nameof(InputEventSource.RawInput)
                    ? KeyStats.Properties.Strings.GameStats_ChannelRawInput
                    : KeyStats.Properties.Strings.GameStats_ChannelHook);

        BuildTimeline(session);
        TopKeys = BuildTopKeys(session, 10);
        HasTopKeys = TopKeys.Count > 0;
    }

    #region Header / metrics

    public string GameName { get; }
    public string TimeText { get; }
    public string DurationText { get; }
    public string CaptureChannelText { get; }

    public string KeysText { get; }
    public string LeftText { get; }
    public string RightText { get; }
    public string MiddleText { get; }
    public string SideText { get; }
    public string ClicksText { get; }
    public string MouseDistanceText { get; }
    public string ScrollText { get; }

    public string AverageApmText { get; }
    public string PeakApmText { get; }
    public string PeakKpmText { get; }
    public string PeakCpmText { get; }
    public string PeakKpsText { get; }

    #endregion

    #region Timeline

    public ObservableCollection<TimelineBarItem> TimelineBars { get; } = new();

    public bool HasTimeline => TimelineBars.Count > 0;

    public bool HasNoTimeline => !HasTimeline;

    private void BuildTimeline(GameSession session)
    {
        var samples = session.MinuteTimeline;
        if (samples == null || samples.Count == 0)
        {
            return;
        }

        var max = Math.Max(1, samples.Select(s => s.Total).DefaultIfEmpty(0).Max());

        foreach (var sample in samples)
        {
            var ratio = sample.Total / (double)max;
            TimelineBars.Add(new TimelineBarItem
            {
                ValueText = sample.Total.ToString("N0", CultureInfo.CurrentCulture),
                TooltipText = string.Format(
                    KeyStats.Properties.Strings.GameStats_MinuteTooltipFormat,
                    sample.Minute,
                    sample.Keys,
                    sample.Clicks),
                MinuteText = sample.Minute.ToString(CultureInfo.CurrentCulture),
                BarHeight = sample.Total <= 0 ? MinBarHeight : Math.Max(MinBarHeight, ratio * MaxBarHeight),
                ShowMinuteLabel = sample.Minute == 1 || sample.Minute % 5 == 0,
                HasValue = sample.Total > 0
            });
        }
    }

    #endregion

    #region Top keys

    public List<KeyValuePair<string, int>> TopKeys { get; }

    public bool HasTopKeys { get; }

    private static List<KeyValuePair<string, int>> BuildTopKeys(GameSession session, int limit)
    {
        return (session.KeyPressCounts ?? new Dictionary<string, int>())
            .Where(x => x.Value > 0 && !string.IsNullOrWhiteSpace(x.Key))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>Raw per-key counts, already filtered to heatmap-supported ids.</summary>
    public Dictionary<string, int> HeatmapCounts
    {
        get
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in _session.KeyPressCounts ?? new Dictionary<string, int>())
            {
                if (pair.Value > 0)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }
    }

    #endregion

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

    private static string FormatCount(int value) =>
        Math.Max(0, value).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatRate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return "0";
        }

        return Math.Round(value).ToString("N0", CultureInfo.CurrentCulture);
    }

    private static string FormatScroll(double value)
    {
        if (value >= 10000)
        {
            return $"{value / 1000:F1} k";
        }

        return value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
