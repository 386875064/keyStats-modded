using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyStats.Models;

/// <summary>
/// 设备寿命估算：用户选择键盘/鼠标类型（含自定义额定次数），
/// 用累计按键/点击数对比额定寿命，得出剩余百分比与预计可用天数。
/// </summary>
public sealed class DeviceLifespanOption
{
    public const string CustomId = "custom";

    public DeviceLifespanOption(string id, string label, long ratedCount)
    {
        Id = id;
        Label = label;
        RatedCount = ratedCount;
    }

    public string Id { get; }
    public string Label { get; }

    /// <summary>额定寿命次数；自定义项为 0，需用用户输入的值替代。</summary>
    public long RatedCount { get; }

    public bool IsCustom => string.Equals(Id, CustomId, StringComparison.Ordinal);
}

public static class DeviceLifespanCatalog
{
    public const long DefaultKeyboardCustomWan = 5000; // 万次
    public const long DefaultMouseCustomWan = 2000;    // 万次

    public static readonly IReadOnlyList<DeviceLifespanOption> Keyboards = new[]
    {
        new DeviceLifespanOption("kb_laptop_5m",    "笔记本键盘（约 500 万次按键）", 5_000_000L),
        new DeviceLifespanOption("kb_membrane_5m",  "薄膜键盘（约 500 万次按键）", 5_000_000L),
        new DeviceLifespanOption("kb_membrane_10m", "薄膜键盘·耐用型（约 1000 万次）", 10_000_000L),
        new DeviceLifespanOption("kb_mech_50m",     "机械键盘·常规轴体（约 5000 万次）", 50_000_000L),
        new DeviceLifespanOption("kb_mech_100m",    "机械键盘·光轴/长寿命轴（约 1 亿次）", 100_000_000L),
        new DeviceLifespanOption(DeviceLifespanOption.CustomId, "自定义…（手动输入额定次数）", 0L),
    };

    public static readonly IReadOnlyList<DeviceLifespanOption> Mice = new[]
    {
        new DeviceLifespanOption("ms_kailh_bgm_90m", "凯华黑金曼巴微动（约 9000 万次点击）", 90_000_000L),
        new DeviceLifespanOption("ms_kailong_black_purple_100m", "凯龙黑透紫微动（约 1 亿次）", 100_000_000L),
        new DeviceLifespanOption("ms_kailong_purple_150m", "凯龙紫壳紫点微动（约 1.5 亿次）", 150_000_000L),
        new DeviceLifespanOption("ms_office_5m",   "办公鼠标（约 500 万次点击）", 5_000_000L),
        new DeviceLifespanOption("ms_gaming_10m",  "游戏鼠标（约 1000 万次）", 10_000_000L),
        new DeviceLifespanOption("ms_esports_20m", "电竞鼠标·欧姆龙 20M 微动（约 2000 万次）", 20_000_000L),
        new DeviceLifespanOption("ms_esports_50m", "高端电竞鼠标·50M 微动（约 5000 万次）", 50_000_000L),
        new DeviceLifespanOption("ms_optical_80m", "光学微动鼠标（约 8000 万次）", 80_000_000L),
        new DeviceLifespanOption(DeviceLifespanOption.CustomId, "自定义…（手动输入额定次数）", 0L),
    };

    public static DeviceLifespanOption FindKeyboard(string? id) => Find(Keyboards, id, Keyboards[0]);

    public static DeviceLifespanOption FindMouse(string? id) => Find(Mice, id, Mice[0]);

    /// <summary>
    /// 解析实际额定次数：预设项直接取值；自定义项取用户输入（单位：万次）。
    /// </summary>
    public static long ResolveRatedCount(DeviceLifespanOption option, long customWan)
    {
        if (!option.IsCustom)
        {
            return option.RatedCount;
        }
        var wan = customWan > 0 ? customWan : 0;
        return wan * 10000L;
    }

    private static DeviceLifespanOption Find(
        IReadOnlyList<DeviceLifespanOption> options,
        string? id,
        DeviceLifespanOption fallback)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var match = options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }
        }
        return fallback;
    }
}
