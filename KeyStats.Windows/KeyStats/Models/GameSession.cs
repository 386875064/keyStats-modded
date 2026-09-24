using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeyStats.Models;

/// <summary>One minute of a session, used to draw the APM timeline.</summary>
public class SessionMinuteSample
{
    /// <summary>1-based minute index inside the session.</summary>
    [JsonPropertyName("minute")]
    public int Minute { get; set; }

    [JsonPropertyName("keys")]
    public int Keys { get; set; }

    [JsonPropertyName("clicks")]
    public int Clicks { get; set; }

    /// <summary>Seconds this bucket actually covered (60 for a regular minute).</summary>
    [JsonPropertyName("seconds")]
    public double Seconds { get; set; } = 60;

    [JsonIgnore]
    public int Total => Keys + Clicks;
}

/// <summary>
/// A single game session (one match / one continuous run of the game process).
/// Sessions are recorded by GameSessionService and persisted to game_sessions.json.
/// </summary>
public class GameSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Stable game identifier, e.g. "lol". Used to resolve localized names.</summary>
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    /// <summary>Localized display name captured when the session was recorded.</summary>
    [JsonPropertyName("gameName")]
    public string GameName { get; set; } = string.Empty;

    /// <summary>Process name that started this session, e.g. "League of Legends".</summary>
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("keyPresses")]
    public int KeyPresses { get; set; }

    [JsonPropertyName("leftClicks")]
    public int LeftClicks { get; set; }

    [JsonPropertyName("rightClicks")]
    public int RightClicks { get; set; }

    [JsonPropertyName("middleClicks")]
    public int MiddleClicks { get; set; }

    [JsonPropertyName("sideBackClicks")]
    public int SideBackClicks { get; set; }

    [JsonPropertyName("sideForwardClicks")]
    public int SideForwardClicks { get; set; }

    [JsonPropertyName("mouseDistance")]
    public double MouseDistance { get; set; }

    [JsonPropertyName("scrollDistance")]
    public double ScrollDistance { get; set; }

    /// <summary>Highest keys-per-minute observed in any single completed minute.</summary>
    [JsonPropertyName("peakKpm")]
    public double PeakKpm { get; set; }

    /// <summary>Highest clicks-per-minute observed in any single completed minute.</summary>
    [JsonPropertyName("peakCpm")]
    public double PeakCpm { get; set; }

    /// <summary>Highest APM (keys + clicks per minute) observed in any single completed minute.</summary>
    [JsonPropertyName("peakApm")]
    public double PeakApm { get; set; }

    /// <summary>Peak instantaneous KPS (1 second window), consistent with the daily peak.</summary>
    [JsonPropertyName("peakKps")]
    public double PeakKps { get; set; }

    /// <summary>Per-key press counts inside this session.</summary>
    [JsonPropertyName("keyPressCounts")]
    public Dictionary<string, int> KeyPressCounts { get; set; } = new();

    /// <summary>Capture channel that fed this session ("RawInput" or "Hook"). Diagnostic only.</summary>
    [JsonPropertyName("captureChannel")]
    public string CaptureChannel { get; set; } = string.Empty;

    /// <summary>Per-minute key/click counts, oldest first.</summary>
    [JsonPropertyName("minuteTimeline")]
    public List<SessionMinuteSample> MinuteTimeline { get; set; } = new();

    /// <summary>False while the session is still running, or when the app crashed mid-session.</summary>
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonIgnore]
    public int TotalClicks =>
        LeftClicks + RightClicks + MiddleClicks + SideBackClicks + SideForwardClicks;

    [JsonIgnore]
    public int TotalInputs => KeyPresses + TotalClicks;

    [JsonIgnore]
    public bool HasActivity =>
        KeyPresses > 0 || TotalClicks > 0 || MouseDistance > 0 || ScrollDistance > 0;

    /// <summary>Average APM across the whole session.</summary>
    [JsonIgnore]
    public double AverageApm =>
        DurationSeconds > 1.0 ? TotalInputs / (DurationSeconds / 60.0) : 0;

    [JsonIgnore]
    public double AverageKpm =>
        DurationSeconds > 1.0 ? KeyPresses / (DurationSeconds / 60.0) : 0;

    [JsonIgnore]
    public double AverageCpm =>
        DurationSeconds > 1.0 ? TotalClicks / (DurationSeconds / 60.0) : 0;

    public GameSession() { }

    public GameSession Clone()
    {
        return new GameSession
        {
            Id = Id,
            GameId = GameId,
            GameName = GameName,
            ProcessName = ProcessName,
            StartTime = StartTime,
            EndTime = EndTime,
            DurationSeconds = DurationSeconds,
            KeyPresses = KeyPresses,
            LeftClicks = LeftClicks,
            RightClicks = RightClicks,
            MiddleClicks = MiddleClicks,
            SideBackClicks = SideBackClicks,
            SideForwardClicks = SideForwardClicks,
            MouseDistance = MouseDistance,
            ScrollDistance = ScrollDistance,
            PeakKpm = PeakKpm,
            PeakCpm = PeakCpm,
            PeakApm = PeakApm,
            PeakKps = PeakKps,
            KeyPressCounts = new Dictionary<string, int>(KeyPressCounts),
            CaptureChannel = CaptureChannel,
            MinuteTimeline = new List<SessionMinuteSample>(MinuteTimeline),
            Completed = Completed
        };
    }
}
