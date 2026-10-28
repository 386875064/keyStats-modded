using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeyStats.Models;

public class AppStats
{
    public string AppName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int KeyPresses { get; set; }
    public int LeftClicks { get; set; }
    public int RightClicks { get; set; }
    public int MiddleClicks { get; set; }
    public int SideBackClicks { get; set; }
    public int SideForwardClicks { get; set; }
    public double ScrollDistance { get; set; }

    /// <summary>该应用内的逐键次数（2026-10-28 起记录；旧数据为空，热力图按应用过滤时会提示）。</summary>
    public Dictionary<string, int> KeyPressCounts { get; set; } = new();

    [JsonIgnore]
    public int TotalClicks => LeftClicks + RightClicks + MiddleClicks + SideBackClicks + SideForwardClicks;

    [JsonIgnore]
    public bool HasActivity =>
        KeyPresses > 0 ||
        LeftClicks > 0 ||
        RightClicks > 0 ||
        MiddleClicks > 0 ||
        SideBackClicks > 0 ||
        SideForwardClicks > 0 ||
        ScrollDistance > 0;

    public AppStats() { }

    public AppStats(string appName, string displayName = "")
    {
        AppName = appName;
        DisplayName = string.IsNullOrEmpty(displayName) ? appName : displayName;
    }

    public AppStats(AppStats source)
    {
        AppName = source.AppName;
        DisplayName = source.DisplayName;
        KeyPresses = source.KeyPresses;
        LeftClicks = source.LeftClicks;
        RightClicks = source.RightClicks;
        MiddleClicks = source.MiddleClicks;
        SideBackClicks = source.SideBackClicks;
        SideForwardClicks = source.SideForwardClicks;
        ScrollDistance = source.ScrollDistance;
        KeyPressCounts = new Dictionary<string, int>(source.KeyPressCounts);
    }

    public void RecordKeyPress() => KeyPresses++;

    public void RecordKeyPress(string? keyName)
    {
        KeyPresses++;
        if (!string.IsNullOrEmpty(keyName))
        {
            KeyPressCounts[keyName] = (KeyPressCounts.TryGetValue(keyName!, out var count) ? count : 0) + 1;
        }
    }
    public void RecordLeftClick() => LeftClicks++;
    public void RecordRightClick() => RightClicks++;
    public void RecordMiddleClick() => MiddleClicks++;
    public void RecordSideBackClick() => SideBackClicks++;
    public void RecordSideForwardClick() => SideForwardClicks++;
    public void AddScrollDistance(double distance) => ScrollDistance += distance;
}
