using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KeyStats.Helpers;
using KeyStats.Models;
using KeyStats.Services;
using KeyStats.Views.Controls;

namespace KeyStats.Views;

public partial class DeviceLifespanWindow : Window
{
    /// <summary>中键 / 侧键通常沿用原装微动，不按用户更换的微动估算。</summary>
    private const long StockSwitchRatedCount = 5_000_000L;

    private bool _isLoading = true;

    public DeviceLifespanWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;

        KeyboardLifespanView.KeyClicked += OnKeyClicked;
        MouseLifespanView.ButtonClicked += OnMouseButtonClicked;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        LoadControls();
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new System.Action(ApplyWindowBackdrop));
    }

    private void ApplyWindowBackdrop()
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
    }

    private void LoadControls()
    {
        _isLoading = true;
        KeyboardTypeComboBox.ItemsSource = DeviceLifespanCatalog.Keyboards;
        MouseTypeComboBox.ItemsSource = DeviceLifespanCatalog.Mice;
        var settings = StatsManager.Instance.Settings;
        KeyboardTypeComboBox.SelectedValue = DeviceLifespanCatalog.FindKeyboard(settings.KeyboardDeviceId).Id;
        MouseTypeComboBox.SelectedValue = DeviceLifespanCatalog.FindMouse(settings.MouseDeviceId).Id;
        KeyboardCustomTextBox.Text = settings.KeyboardCustomRatedWan.ToString();
        MouseCustomTextBox.Text = settings.MouseCustomRatedWan.ToString();
        _isLoading = false;
        RefreshDisplay();
    }

    private void DeviceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var settings = StatsManager.Instance.Settings;
        if (KeyboardTypeComboBox.SelectedValue is string keyboardId)
        {
            settings.KeyboardDeviceId = keyboardId;
        }
        if (MouseTypeComboBox.SelectedValue is string mouseId)
        {
            settings.MouseDeviceId = mouseId;
        }
        StatsManager.Instance.SaveSettings();
        RefreshDisplay();
    }

    private void CustomRated_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var settings = StatsManager.Instance.Settings;
        var changed = false;
        if (ReferenceEquals(sender, KeyboardCustomTextBox))
        {
            if (long.TryParse(KeyboardCustomTextBox.Text, out var wan) && wan > 0)
            {
                settings.KeyboardCustomRatedWan = wan;
                changed = true;
            }
            KeyboardCustomTextBox.Text = settings.KeyboardCustomRatedWan.ToString();
        }
        else if (ReferenceEquals(sender, MouseCustomTextBox))
        {
            if (long.TryParse(MouseCustomTextBox.Text, out var wan) && wan > 0)
            {
                settings.MouseCustomRatedWan = wan;
                changed = true;
            }
            MouseCustomTextBox.Text = settings.MouseCustomRatedWan.ToString();
        }

        if (changed)
        {
            StatsManager.Instance.SaveSettings();
            RefreshDisplay();
        }
    }

    private void RefreshDisplay()
    {
        var settings = StatsManager.Instance.Settings;
        var keyboardOption = DeviceLifespanCatalog.FindKeyboard(
            KeyboardTypeComboBox.SelectedValue as string ?? settings.KeyboardDeviceId);
        var mouseOption = DeviceLifespanCatalog.FindMouse(
            MouseTypeComboBox.SelectedValue as string ?? settings.MouseDeviceId);

        KeyboardCustomPanel.Visibility = keyboardOption.IsCustom ? Visibility.Visible : Visibility.Collapsed;
        MouseCustomPanel.Visibility = mouseOption.IsCustom ? Visibility.Visible : Visibility.Collapsed;

        var keyboardRated = DeviceLifespanCatalog.ResolveRatedCount(keyboardOption, settings.KeyboardCustomRatedWan);
        var mouseRated = DeviceLifespanCatalog.ResolveRatedCount(mouseOption, settings.MouseCustomRatedWan);

        var calendarDays = StatsManager.Instance.GetRecordedCalendarDays();

        // 键盘面板：额定是「单键寿命」口径 —— 主百分比用平均键位磨损，预估按最坏键
        if (keyboardRated > 0)
        {
            var keyCounts = GetAdjustedKeyCounts();
            KeyboardLifespanView.ApplyLifespan(keyCounts, keyboardRated);

            var worst = keyCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            KeyboardWearSummary.Text = worst.Key != null
                ? $"磨损最多的键：{worst.Key}（{FormatCount(worst.Value)} 次，剩余 {System.Math.Max(0.0, 100.0 - worst.Value * 100.0 / keyboardRated):F1}%）"
                : "还没有按键数据";

            ApplyKeyboardSummary(keyboardRated, keyCounts, calendarDays);
        }
        else
        {
            KeyboardLifespanView.ApplyLifespan(null, 1);
            KeyboardWearSummary.Text = "请先输入有效的键盘额定寿命（万次）";
            ApplyKeyboardSummary(0, new Dictionary<string, int>(), 0);
        }

        // 鼠标面板：每颗微动独立额定 —— 主百分比用最坏微动（木桶）
        if (mouseRated > 0)
        {
            var clicks = GetAdjustedClicks();
            MouseLifespanView.Apply(
                new MouseLifespanControl.ButtonWear(clicks.Left, mouseRated),
                new MouseLifespanControl.ButtonWear(clicks.Right, mouseRated),
                new MouseLifespanControl.ButtonWear(clicks.Middle, StockSwitchRatedCount),
                new MouseLifespanControl.ButtonWear(clicks.SideForward + clicks.SideBack, StockSwitchRatedCount));

            ApplyMouseSummary(mouseRated, clicks, calendarDays);
        }
        else
        {
            MouseLifespanView.Apply(
                new MouseLifespanControl.ButtonWear(0, 1),
                new MouseLifespanControl.ButtonWear(0, 1),
                new MouseLifespanControl.ButtonWear(0, 1),
                new MouseLifespanControl.ButtonWear(0, 1));
            ApplyMouseSummary(0, default, 0);
        }
    }

    /// <summary>逐键次数扣除每个键的重置基线（换键后从 0 重算，原始数据不动）。</summary>
    private static Dictionary<string, int> GetAdjustedKeyCounts()
    {
        var baselines = StatsManager.Instance.Settings.KeyPressResetBaselines;
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kvp in StatsManager.Instance.GetLifetimeKeyCounts())
        {
            var baseline = baselines.TryGetValue(kvp.Key, out var value) ? value : 0;
            var adjusted = kvp.Value - baseline;
            if (adjusted > 0)
            {
                result[kvp.Key] = (int)System.Math.Min(int.MaxValue, adjusted);
            }
        }

        return result;
    }

    /// <summary>鼠标各分区点击扣除重置基线。</summary>
    private static (long Left, long Right, long Middle, long SideForward, long SideBack) GetAdjustedClicks()
    {
        var baselines = StatsManager.Instance.Settings.MouseClickResetBaselines;
        var raw = StatsManager.Instance.GetLifetimeClickBreakdown();

        long Adjust(string key, long value)
        {
            var baseline = baselines.TryGetValue(key, out var stored) ? stored : 0;
            return System.Math.Max(0, value - baseline);
        }

        return (
            Adjust("Left", raw.Left),
            Adjust("Right", raw.Right),
            Adjust("Middle", raw.Middle),
            Adjust("Side", raw.SideForward),
            Adjust("Side", raw.SideBack));
    }

    // ===== 寿命重置 =====

    private void OnKeyClicked(string keyId)
    {
        var raw = StatsManager.Instance.GetLifetimeKeyCounts();
        var used = raw.TryGetValue(keyId, out var value) ? value : 0;
        if (used <= 0)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            $"重置「{keyId}」的磨损？\n\n当前累计 {used:N0} 次将作为新基线，之后该键从 0 重算。\n原始统计数据不会删除，其它报表不受影响。",
            "重置按键寿命",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        var settings = StatsManager.Instance.Settings;
        settings.KeyPressResetBaselines[keyId] = used;
        StatsManager.Instance.SaveSettings();
        RefreshDisplay();
    }

    private void OnMouseButtonClicked(string kind)
    {
        var label = kind switch
        {
            "Left" => "左键",
            "Right" => "右键",
            "Middle" => "中键",
            _ => "侧键"
        };

        var raw = StatsManager.Instance.GetLifetimeClickBreakdown();
        var used = kind switch
        {
            "Left" => raw.Left,
            "Right" => raw.Right,
            "Middle" => raw.Middle,
            _ => raw.SideForward + raw.SideBack
        };
        if (used <= 0)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            $"重置鼠标「{label}」的磨损？\n\n当前累计 {used:N0} 次将作为新基线，之后从 0 重算。\n原始统计数据不会删除，其它报表不受影响。",
            "重置鼠标按键寿命",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        var settings = StatsManager.Instance.Settings;
        settings.MouseClickResetBaselines[kind] = used;
        StatsManager.Instance.SaveSettings();
        RefreshDisplay();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "清除所有重置基线，恢复按全量累计计算？\n\n（已经换过的键会重新显示旧键的累计磨损）",
            "重置全部",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        var settings = StatsManager.Instance.Settings;
        settings.KeyPressResetBaselines.Clear();
        settings.MouseClickResetBaselines.Clear();
        StatsManager.Instance.SaveSettings();
        RefreshDisplay();
    }

    private void ApplyKeyboardSummary(long ratedPerKey, Dictionary<string, int> keyCounts, int calendarDays)
    {
        if (ratedPerKey <= 0)
        {
            KeyboardBigPercent.Text = "—";
            KeyboardLifespanText.Text = "请先输入有效的额定寿命（万次）";
            KeyboardLifespanDaysText.Text = string.Empty;
            KeyboardLifespanBar.Value = 0;
            return;
        }

        var totalKeys = 0L;
        foreach (var kvp in keyCounts)
        {
            totalKeys += kvp.Value;
        }
        var usedKeyCount = System.Math.Max(1, keyCounts.Count);
        var avgPerKey = totalKeys / (double)usedKeyCount;

        var remainPercent = System.Math.Max(0.0, 100.0 - avgPerKey * 100.0 / ratedPerKey);
        KeyboardBigPercent.Text = $"{remainPercent:F1}%";
        KeyboardLifespanText.Text =
            $"平均每键已用 {FormatCount((long)avgPerKey)} / 单键额定 {FormatCount(ratedPerKey)} 次（全盘 {FormatCount(totalKeys)} · {usedKeyCount} 键）";
        KeyboardLifespanBar.Value = System.Math.Max(0, System.Math.Min(100, remainPercent));

        var worst = keyCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        if (worst.Key == null || worst.Value <= 0)
        {
            KeyboardLifespanDaysText.Text = "还没有按键数据";
            return;
        }

        var worstRemaining = System.Math.Max(0, ratedPerKey - worst.Value);
        if (worstRemaining <= 0)
        {
            KeyboardLifespanDaysText.Text = "最坏键已达标称寿命，建议更换";
            return;
        }

        var worstPerDay = worst.Value / (double)System.Math.Max(1, calendarDays);
        var daysLeft = worstRemaining / worstPerDay;
        KeyboardLifespanDaysText.Text = daysLeft >= 365
            ? $"按最坏键 {worst.Key}（日均 {worstPerDay:F0} 次），≈ 还能用 {daysLeft / 365:F1} 年"
            : $"按最坏键 {worst.Key}（日均 {worstPerDay:F0} 次），≈ 还能用 {daysLeft:F0} 天";
    }

    private void ApplyMouseSummary(
        long ratedPerSwitch,
        (long Left, long Right, long Middle, long SideForward, long SideBack) clicks,
        int calendarDays)
    {
        if (ratedPerSwitch <= 0)
        {
            MouseBigPercent.Text = "—";
            MouseLifespanText.Text = "请先输入有效的额定寿命（万次）";
            MouseLifespanDaysText.Text = string.Empty;
            MouseLifespanBar.Value = 0;
            return;
        }

        // 每颗微动独立额定：左 / 右 = 所选微动，中 / 侧 = 原装 500 万（侧键两颗合并估）
        var buttons = new (string Name, long Used, long Rated)[]
        {
            ("左键", clicks.Left, ratedPerSwitch),
            ("右键", clicks.Right, ratedPerSwitch),
            ("中键", clicks.Middle, StockSwitchRatedCount),
            ("侧键", clicks.SideForward + clicks.SideBack, StockSwitchRatedCount),
        };
        var totalClicks = clicks.Left + clicks.Right + clicks.Middle + clicks.SideForward + clicks.SideBack;
        var worst = buttons.OrderByDescending(b => b.Used * 100.0 / System.Math.Max(1, b.Rated)).First();

        var remainPercent = System.Math.Max(0.0, 100.0 - worst.Used * 100.0 / System.Math.Max(1, worst.Rated));
        MouseBigPercent.Text = $"{remainPercent:F1}%";
        MouseLifespanText.Text =
            $"最坏微动 {worst.Name} 已用 {FormatCount(worst.Used)} / 额定 {FormatCount(worst.Rated)} 次（全鼠标 {FormatCount(totalClicks)}）";
        MouseLifespanBar.Value = System.Math.Max(0, System.Math.Min(100, remainPercent));

        var remaining = System.Math.Max(0, worst.Rated - worst.Used);
        if (remaining <= 0)
        {
            MouseLifespanDaysText.Text = "最坏微动已达标称寿命，建议更换";
            return;
        }

        var perDay = worst.Used / (double)System.Math.Max(1, calendarDays);
        if (perDay <= 0)
        {
            MouseLifespanDaysText.Text = "暂无使用数据，无法估算天数";
            return;
        }

        var daysLeft = remaining / perDay;
        MouseLifespanDaysText.Text = daysLeft >= 365
            ? $"按最坏微动 {worst.Name}（日均 {perDay:F0} 次），≈ 还能用 {daysLeft / 365:F1} 年"
            : $"按最坏微动 {worst.Name}（日均 {perDay:F0} 次），≈ 还能用 {daysLeft:F0} 天";
    }

    private static string FormatCount(long value)
    {
        if (value >= 100_000_000L)
        {
            return $"{value / 100_000_000.0:F2} 亿";
        }
        if (value >= 10_000L)
        {
            return $"{value / 10_000.0:F1} 万";
        }
        return value.ToString("N0");
    }
}
