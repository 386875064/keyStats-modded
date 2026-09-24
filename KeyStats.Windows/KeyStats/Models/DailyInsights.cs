using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeyStats.Models;

/// <summary>Per-application foreground usage for a single day.</summary>
public class AppFocusEntry
{
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Total seconds this app owned the foreground.</summary>
    [JsonPropertyName("seconds")]
    public double Seconds { get; set; }

    /// <summary>Longest uninterrupted stretch in the foreground.</summary>
    [JsonPropertyName("longestStreakSeconds")]
    public double LongestStreakSeconds { get; set; }
}

/// <summary>
/// Daily usage insights: typing quality, focus, working hours and continuous-use
/// streaks. Stored separately from DailyStats so it never touches sync or history.
/// </summary>
public class DailyInsights
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("totalKeys")]
    public int TotalKeys { get; set; }

    [JsonPropertyName("backspaceKeys")]
    public int BackspaceKeys { get; set; }

    /// <summary>Presses of Ctrl / Shift / Alt / Win — a rough shortcut-usage signal.</summary>
    [JsonPropertyName("modifierKeys")]
    public int ModifierKeys { get; set; }

    [JsonPropertyName("totalClicks")]
    public int TotalClicks { get; set; }

    /// <summary>How many times the foreground application changed.</summary>
    [JsonPropertyName("appSwitches")]
    public int AppSwitches { get; set; }

    /// <summary>Key presses bucketed by hour of day (local time).</summary>
    [JsonPropertyName("hourlyKeys")]
    public int[] HourlyKeys { get; set; } = new int[24];

    [JsonPropertyName("hourlyClicks")]
    public int[] HourlyClicks { get; set; } = new int[24];

    [JsonPropertyName("apps")]
    public Dictionary<string, AppFocusEntry> Apps { get; set; } = new();

    /// <summary>Total seconds during which input was actively arriving.</summary>
    [JsonPropertyName("activeSeconds")]
    public double ActiveSeconds { get; set; }

    /// <summary>Current continuous-use streak (resets after an idle gap).</summary>
    [JsonPropertyName("currentStreakSeconds")]
    public double CurrentStreakSeconds { get; set; }

    [JsonPropertyName("longestStreakSeconds")]
    public double LongestStreakSeconds { get; set; }

    /// <summary>True once the long-session reminder fired for the current streak.</summary>
    [JsonPropertyName("breakReminderSent")]
    public bool BreakReminderSent { get; set; }

    [JsonIgnore]
    public int NetTypingKeys => Math.Max(0, TotalKeys - BackspaceKeys);

    /// <summary>Backspace presses as a share of all key presses (typing accuracy proxy).</summary>
    [JsonIgnore]
    public double BackspaceRatio => TotalKeys > 0 ? BackspaceKeys / (double)TotalKeys : 0;

    public DailyInsights() { }

    public DailyInsights(DateTime date)
    {
        Date = date.Date;
    }

    public void EnsureShape()
    {
        if (HourlyKeys == null || HourlyKeys.Length != 24)
        {
            var copy = new int[24];
            if (HourlyKeys != null)
            {
                Array.Copy(HourlyKeys, copy, Math.Min(24, HourlyKeys.Length));
            }
            HourlyKeys = copy;
        }

        if (HourlyClicks == null || HourlyClicks.Length != 24)
        {
            var copy = new int[24];
            if (HourlyClicks != null)
            {
                Array.Copy(HourlyClicks, copy, Math.Min(24, HourlyClicks.Length));
            }
            HourlyClicks = copy;
        }

        if (Apps == null)
        {
            Apps = new Dictionary<string, AppFocusEntry>();
        }
    }
}
