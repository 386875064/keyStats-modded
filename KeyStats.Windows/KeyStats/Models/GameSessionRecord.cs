using System;
using System.Text.Json.Serialization;

namespace KeyStats.Models;

/// <summary>
/// 一局游戏的输入统计。字段名与上游版本保持一致，可直接读取旧的 game_sessions.json。
/// </summary>
public class GameSessionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = "lol";

    [JsonPropertyName("gameName")]
    public string GameName { get; set; } = "英雄联盟";

    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = "League of Legends";

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset EndTime { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("keyPresses")]
    public long KeyPresses { get; set; }

    [JsonPropertyName("leftClicks")]
    public long LeftClicks { get; set; }

    [JsonPropertyName("rightClicks")]
    public long RightClicks { get; set; }

    [JsonPropertyName("middleClicks")]
    public long MiddleClicks { get; set; }

    [JsonPropertyName("sideBackClicks")]
    public long SideBackClicks { get; set; }

    [JsonPropertyName("sideForwardClicks")]
    public long SideForwardClicks { get; set; }

    [JsonPropertyName("mouseDistance")]
    public double MouseDistance { get; set; }

    [JsonPropertyName("scrollDistance")]
    public double ScrollDistance { get; set; }

    [JsonPropertyName("peakKpm")]
    public int PeakKpm { get; set; }

    [JsonPropertyName("peakCpm")]
    public int PeakCpm { get; set; }

    [JsonPropertyName("peakApm")]
    public int PeakApm { get; set; }

    [JsonPropertyName("captureChannel")]
    public string CaptureChannel { get; set; } = "Hook";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; } = true;

    [JsonIgnore]
    public long TotalClicks => LeftClicks + RightClicks + MiddleClicks + SideBackClicks + SideForwardClicks;

    [JsonIgnore]
    public double ApM => DurationSeconds > 0
        ? (KeyPresses + TotalClicks) / (DurationSeconds / 60.0)
        : 0;

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            var total = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
            return total.TotalHours >= 1
                ? $"{(int)total.TotalHours}:{total.Minutes:D2}:{total.Seconds:D2}"
                : $"{total.Minutes:D2}:{total.Seconds:D2}";
        }
    }

    [JsonIgnore]
    public string ChannelText => string.Equals(CaptureChannel, "Raw Input", StringComparison.OrdinalIgnoreCase)
        ? "Raw Input"
        : "钩子";

    [JsonIgnore]
    public string DateText => StartTime.LocalDateTime.ToString("MM-dd HH:mm");

    [JsonIgnore]
    public string DistanceText
    {
        get
        {
            var meters = MouseDistance * AppSettings.DefaultMouseMetersPerPixel;
            if (meters >= 1000)
            {
                return $"{meters / 1000:F2} km";
            }
            if (meters >= 1)
            {
                return $"{meters:F1} m";
            }
            return $"{MouseDistance:F0} px";
        }
    }
}
