using System;
using System.Text.Json.Serialization;

namespace KeyStats.Models;

public class AppSettings
{
    public const double DefaultMouseMetersPerPixel = 0.00005;
    public const string FloatingStatsSingleRowLayoutMode = "singleRow";
    public const string FloatingStatsDoubleRowLayoutMode = "doubleRow";
    public const int FloatingStatsLayoutBaseFontSize = 11;
    public const int DefaultFloatingStatsFontSize = 12;
    public const int MinimumFloatingStatsFontSize = 9;
    public const int MaximumFloatingStatsFontSize = 22;
    private int _floatingStatsFontSize = DefaultFloatingStatsFontSize;

    [JsonPropertyName("notificationsEnabled")]
    public bool NotificationsEnabled { get; set; }

    [JsonPropertyName("keyPressNotifyThreshold")]
    public int KeyPressNotifyThreshold { get; set; } = 1000;

    [JsonPropertyName("clickNotifyThreshold")]
    public int ClickNotifyThreshold { get; set; } = 1000;

    [JsonPropertyName("launchAtStartup")]
    public bool LaunchAtStartup { get; set; }

    /// <summary>
    /// Anonymous usage analytics. Off by default in this build: the upstream key
    /// belongs to the original author's PostHog project, so shipping it would send
    /// other people's usage data to a third party. Fill in your own key to enable.
    /// </summary>
    [JsonPropertyName("analyticsEnabled")]
    public bool AnalyticsEnabled { get; set; }

    [JsonPropertyName("analyticsApiKey")]
    public string? AnalyticsApiKey { get; set; }

    /// <summary>
    /// Upstream author's hard-coded PostHog key. Kept only so the app can recognise
    /// and purge it from existing settings files (see the migration in App.OnStartup).
    /// </summary>
    public const string LegacyUpstreamAnalyticsApiKey = "phc_TYyyKIfGgL1CXZx7t9dY7igE3yNwNpjj9aqItSpNVLx";

    [JsonPropertyName("analyticsHost")]
    public string? AnalyticsHost { get; set; }

    [JsonPropertyName("analyticsDistinctId")]
    public string? AnalyticsDistinctId { get; set; }

    [JsonPropertyName("analyticsFirstOpenUtc")]
    public DateTime? AnalyticsFirstOpenUtc { get; set; }

    [JsonPropertyName("analyticsInstallTracked")]
    public bool AnalyticsInstallTracked { get; set; }

    [JsonPropertyName("mouseMetersPerPixel")]
    public double MouseMetersPerPixel { get; set; } = DefaultMouseMetersPerPixel;

    [JsonPropertyName("mouseDistanceUnit")]
    public string MouseDistanceUnit { get; set; } = "auto"; // auto | px

    [JsonPropertyName("keyHistorySelectedRangeIndex")]
    public int KeyHistorySelectedRangeIndex { get; set; } = 1;

    [JsonPropertyName("mainWindowLeft")]
    public double? MainWindowLeft { get; set; }

    [JsonPropertyName("mainWindowTop")]
    public double? MainWindowTop { get; set; }

    [JsonPropertyName("mainWindowWidth")]
    public double? MainWindowWidth { get; set; }

    [JsonPropertyName("mainWindowHeight")]
    public double? MainWindowHeight { get; set; }

    [JsonPropertyName("floatingStatsEnabled")]
    public bool FloatingStatsEnabled { get; set; }

    [JsonPropertyName("floatingStatsPrimaryMetric")]
    public string FloatingStatsPrimaryMetric { get; set; } = "keyPresses";

    [JsonPropertyName("floatingStatsSecondaryMetric")]
    public string FloatingStatsSecondaryMetric { get; set; } = "totalClicks";

    [JsonPropertyName("floatingStatsLayoutMode")]
    public string FloatingStatsLayoutMode { get; set; } = FloatingStatsDoubleRowLayoutMode;

    [JsonPropertyName("floatingStatsFontSize")]
    public int FloatingStatsFontSize
    {
        get => _floatingStatsFontSize;
        set => _floatingStatsFontSize = Math.Max(
            MinimumFloatingStatsFontSize,
            Math.Min(MaximumFloatingStatsFontSize, value));
    }

    [JsonPropertyName("floatingStatsLeft")]
    public double? FloatingStatsLeft { get; set; }

    [JsonPropertyName("floatingStatsTop")]
    public double? FloatingStatsTop { get; set; }

    [JsonPropertyName("floatingStatsMonitorDeviceName")]
    public string? FloatingStatsMonitorDeviceName { get; set; }

    [JsonPropertyName("floatingStatsTopmost")]
    public bool FloatingStatsTopmost { get; set; } = true;

    [JsonPropertyName("floatingStatsPositionLocked")]
    public bool FloatingStatsPositionLocked { get; set; }

    [JsonPropertyName("languagePreference")]
    public string LanguagePreference { get; set; } = "system";  // "system" | "zh-Hans" | "zh-Hant" | "en"

    // ===== Game session tracking =====

    /// <summary>Track dedicated per-game sessions (League of Legends by default).</summary>
    [JsonPropertyName("gameStatsEnabled")]
    public bool GameStatsEnabled { get; set; } = true;

    /// <summary>Show the compact in-game HUD while a game session is running.</summary>
    [JsonPropertyName("gameOverlayEnabled")]
    public bool GameOverlayEnabled { get; set; } = true;

    /// <summary>
    /// When true, input is only counted while a game window owns the foreground.
    /// Off by default so exclusive-fullscreen games are never under-counted.
    /// </summary>
    [JsonPropertyName("gameStatsFrontmostOnly")]
    public bool GameStatsFrontmostOnly { get; set; }

    /// <summary>
    /// When true, input recorded during a game session is also added to the daily
    /// totals / per-app breakdown (the game session list stays independent either way).
    /// </summary>
    [JsonPropertyName("gameStatsMergeIntoDaily")]
    public bool GameStatsMergeIntoDaily { get; set; } = true;

    [JsonPropertyName("gameOverlayLeft")]
    public double? GameOverlayLeft { get; set; }

    [JsonPropertyName("gameOverlayTop")]
    public double? GameOverlayTop { get; set; }

    [JsonPropertyName("gameOverlayMonitorDeviceName")]
    public string? GameOverlayMonitorDeviceName { get; set; }

    [JsonPropertyName("gameOverlayPositionLocked")]
    public bool GameOverlayPositionLocked { get; set; }

    // ===== Daily insights / wellness =====

    /// <summary>Remind me to take a break after a long uninterrupted session.</summary>
    [JsonPropertyName("wellnessReminderEnabled")]
    public bool WellnessReminderEnabled { get; set; } = true;

    /// <summary>Continuous-use threshold, in minutes.</summary>
    [JsonPropertyName("wellnessReminderMinutes")]
    public int WellnessReminderMinutes { get; set; } = 90;
}
