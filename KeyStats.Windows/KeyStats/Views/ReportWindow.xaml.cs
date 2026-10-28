using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KeyStats.Helpers;
using KeyStats.Services;

namespace KeyStats.Views;

public partial class ReportWindow : Window
{
    /// <summary>拼音输入法打一个汉字大约按 3 个键，用来把按键数折算成"写了多少字"。</summary>
    private const double KeysPerCharacter = 3.0;

    private sealed class BarRow
    {
        public string Key { get; set; } = string.Empty;
        public string CountText { get; set; } = string.Empty;
        public double BarWidth { get; set; }
    }

    private sealed class HourRow
    {
        public string Label { get; set; } = string.Empty;
        public double BarHeight { get; set; }
    }

    private string _range = "week";

    public ReportWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
        Refresh();
    }

    private void Range_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _range = tag;
            Refresh();
        }
    }

    private void Refresh()
    {
        var today = DateTime.Today;
        DateTime start;
        DateTime end;
        string title;
        DateTime previousStart;
        DateTime previousEnd;

        switch (_range)
        {
            case "lastweek":
                var lastWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 7);
                start = lastWeekStart;
                end = lastWeekStart.AddDays(6);
                previousStart = start.AddDays(-7);
                previousEnd = end.AddDays(-7);
                title = "上周";
                break;
            case "month":
                start = new DateTime(today.Year, today.Month, 1);
                end = today;
                previousStart = start.AddMonths(-1);
                previousEnd = start.AddDays(-1);
                title = "本月";
                break;
            default:
                start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                end = today;
                previousStart = start.AddDays(-7);
                previousEnd = end.AddDays(-7);
                title = "本周";
                break;
        }

        // 周期按钮高亮
        HighlightRangeButton();

        var summary = StatsManager.Instance.GetPeriodSummary(start, end);
        var previous = StatsManager.Instance.GetPeriodSummary(previousStart, previousEnd);

        RangeText.Text = $"{title}（{start:MM-dd} ~ {end:MM-dd}）";
        RangeKeysText.Text = summary.KeyPresses.ToString("N0");
        KeysText.Text = summary.KeyPresses.ToString("N0");
        ClicksText.Text = summary.Clicks.ToString("N0");
        ActiveDaysText.Text = summary.ActiveDays.ToString("N0");

        var days = Math.Max(1, summary.ActiveDays);
        DailyAvgText.Text = (summary.KeyPresses / (double)days).ToString("N0");
        PeakDayText.Text = summary.PeakDate.HasValue
            ? $"{summary.PeakDate.Value:MM-dd} · {summary.PeakKeys:N0}"
            : "—";

        // 与上一周期对比
        if (previous.KeyPresses > 0)
        {
            var delta = (summary.KeyPresses - previous.KeyPresses) * 100.0 / previous.KeyPresses;
            CompareText.Text = $"{(delta >= 0 ? "+" : string.Empty)}{delta:F0}%";
            CompareText.Foreground = delta >= 0
                ? System.Windows.Media.Brushes.IndianRed
                : (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        }
        else
        {
            CompareText.Text = "—";
        }

        // 搜狗式类比
        AnalogyText.Text = BuildPeriodAnalogy(summary.KeyPresses);
        var lifetime = StatsManager.Instance.GetLifetimeTotals();
        LifetimeAnalogyText.Text = "累计 " + lifetime.KeyPresses.ToString("N0") + " 次按键，" +
                                   BuildLifetimeAnalogy(lifetime.KeyPresses);

        // Top 键
        var topKeys = summary.TopKeys
            .OrderByDescending(pair => pair.Value)
            .Take(10)
            .ToList();
        if (topKeys.Count == 0)
        {
            TopKeysList.ItemsSource = null;
            TopKeysEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            var max = Math.Max(1, topKeys[0].Value);
            var rows = topKeys
                .Select(pair => new BarRow
                {
                    Key = pair.Key,
                    CountText = pair.Value.ToString("N0"),
                    BarWidth = Math.Max(6, 160.0 * pair.Value / max)
                })
                .ToList();
            TopKeysList.ItemsSource = rows;
            TopKeysEmptyText.Visibility = Visibility.Collapsed;
        }

        // 今日 24 小时分布
        RefreshHourly();

        // 备份状态（云端 Supabase，失败会自动降级本地）
        BackupHintText.Text = "备份：" + BackupService.LastStatus + "（云端 Supabase · 坑货雷达项目）";
    }

    private void RefreshHourly()
    {
        var hourly = StatsManager.Instance.GetTodayHourlyKeyPresses();
        if (hourly.Count == 0)
        {
            HourlyList.ItemsSource = null;
            HourlyHintText.Text = "今日暂无小时数据（该功能从今日起开始记录）";
            return;
        }

        var max = Math.Max(1, hourly.Values.Max());
        var rows = new List<HourRow>();
        for (var hour = 0; hour < 24; hour++)
        {
            hourly.TryGetValue(hour, out var count);
            rows.Add(new HourRow
            {
                Label = hour % 3 == 0 ? hour.ToString("D2") : string.Empty,
                BarHeight = count <= 0 ? 2 : Math.Max(4, 60.0 * count / max)
            });
        }

        HourlyList.ItemsSource = rows;
        HourlyHintText.Text = $"今日 {hourly.Values.Sum():N0} 次按键 · 峰值时段 {hourly.OrderByDescending(p => p.Value).First().Key:D2} 点";
    }

    private void HighlightRangeButton()
    {
        var normal = (Style)FindResource("GhostButton");
        ThisWeekButton.Style = normal;
        LastWeekButton.Style = normal;
        MonthButton.Style = normal;
        var active = _range switch
        {
            "lastweek" => LastWeekButton,
            "month" => MonthButton,
            _ => ThisWeekButton
        };
        active.FontWeight = FontWeights.SemiBold;
    }

    /// <summary>本期的"这是什么概念"文案。</summary>
    private static string BuildPeriodAnalogy(long keys)
    {
        if (keys <= 0)
        {
            return "本期还没有按键记录";
        }

        var chars = keys / KeysPerCharacter;
        // 依据：一次按键（按下+复位）手指平均位移按 2 cm 估算，只统计键盘、不含鼠标
        var fingers = keys * 0.02 / 1000.0;
        var text = AnalogizeCharacters(chars);
        return fingers >= 1
            ? $"{text}；手指累计移动约 {fingers:F1} 公里（按每次按键约 2 cm 估算）"
            : text;
    }

    /// <summary>累计的"这是什么概念"文案。</summary>
    private static string BuildLifetimeAnalogy(long keys)
    {
        var chars = keys / KeysPerCharacter;
        return AnalogizeCharacters(chars);
    }

    /// <summary>把字数折成具体作品（选的都是好认的，字数取常见版本的大约值）。</summary>
    private static string AnalogizeCharacters(double chars)
    {
        if (chars >= 5_300_000)
        {
            return $"相当于写完一部《斗破苍穹》（约 530 万字）";
        }
        if (chars >= 1_040_000)
        {
            return $"相当于写完《平凡的世界》（约 104 万字）";
        }
        if (chars >= 880_000)
        {
            return $"相当于写完《三体》三部曲（约 88 万字）";
        }
        if (chars >= 730_000)
        {
            return $"相当于完整抄一遍《红楼梦》（约 73 万字）";
        }
        if (chars >= 500_000)
        {
            return $"相当于写完《白鹿原》（约 50 万字）";
        }
        if (chars >= 300_000)
        {
            return $"相当于写完《三体》第一部（约 30 万字）";
        }
        if (chars >= 250_000)
        {
            return $"相当于写完《围城》（约 25 万字）";
        }
        if (chars >= 120_000)
        {
            return $"相当于写完《活着》（约 12 万字）";
        }
        if (chars >= 30_000)
        {
            return $"相当于写了一部长篇专栏（约 {FormatChars(chars)} 字）";
        }
        if (chars >= 10_000)
        {
            return $"相当于写完一篇毕业论文（约 {FormatChars(chars)} 字）";
        }
        if (chars >= 3_000)
        {
            return $"相当于写了一篇长文（约 {FormatChars(chars)} 字）";
        }
        if (chars >= 800)
        {
            return $"相当于写了几条长微信（约 {FormatChars(chars)} 字）";
        }
        return $"约 {FormatChars(chars)} 字，日常几句话的量";
    }

    private static string FormatChars(double chars)
    {
        return chars >= 10_000 ? $"{chars / 10_000:F1} 万" : $"{chars:F0}";
    }
}
