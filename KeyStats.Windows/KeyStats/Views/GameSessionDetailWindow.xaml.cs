using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using KeyStats.Helpers;
using KeyStats.Models;
using KeyStats.Services;

namespace KeyStats.Views;

public partial class GameSessionDetailWindow : Window
{
    private sealed class KeyRow
    {
        public string Key { get; set; } = string.Empty;
        public string CountText { get; set; } = string.Empty;
        public string PercentText { get; set; } = string.Empty;
    }

    private readonly GameSessionRecord _record;

    public GameSessionDetailWindow(GameSessionRecord record)
    {
        InitializeComponent();
        _record = record;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
        Fill();
    }

    private void Fill()
    {
        ChampionNameText.Text = _record.ChampionText;
        ChangeChampionButton.Content = _record.ChampionId > 0 ? "更改英雄" : "选择英雄";
        DateText.Text = _record.StartTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        DurationText.Text = $"时长 {_record.DurationText}";

        if (_record.ChampionId > 0)
        {
            IconImage.Source = LoadImage(GameSessionService.GetChampionIconPath(_record.ChampionId));
        }

        KeyPressesText.Text = _record.KeyPresses.ToString("N0");
        // 总数独占一行，分项放小字，避免挤爆三等分列宽和 APM 重叠
        ClicksText.Text = _record.TotalClicks.ToString("N0");
        ClicksBreakdownText.Text =
            $"左 {_record.LeftClicks:N0} · 右 {_record.RightClicks:N0} · 中 {_record.MiddleClicks:N0} · 侧 {_record.SideBackClicks + _record.SideForwardClicks:N0}";
        ApmText.Text = _record.ApM.ToString("F0");
        PeakApmText.Text = _record.PeakApm.ToString("N0");
        ChannelText.Text = _record.ChannelText;

        var counts = _record.KeyPressCounts;
        if (counts == null || counts.Count == 0)
        {
            KeyListHeader.Text = "本局逐键分布（该记录没有逐键数据）";
            KeyList.ItemsSource = null;
            return;
        }

        var total = counts.Values.Sum(value => (long)value);
        var rows = counts
            .OrderByDescending(kvp => kvp.Value)
            .Take(30)
            .Select(kvp => new KeyRow
            {
                Key = kvp.Key,
                CountText = kvp.Value.ToString("N0"),
                PercentText = total > 0 ? (kvp.Value * 100.0 / total).ToString("F1") + "%" : "-"
            })
            .ToList<KeyRow>();

        var rest = counts.Count - rows.Count;
        if (rest > 0)
        {
            var restCount = counts.Values.OrderByDescending(v => v).Skip(rows.Count).Sum(v => (long)v);
            rows.Add(new KeyRow
            {
                Key = $"其它 {rest} 键",
                CountText = restCount.ToString("N0"),
                PercentText = total > 0 ? (restCount * 100.0 / total).ToString("F1") + "%" : "-"
            });
        }

        KeyListHeader.Text = $"本局逐键分布（共 {counts.Count} 键）";
        KeyList.ItemsSource = rows;
    }

    private async void ChangeChampionButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ChampionPickerWindow { Owner = this };
        picker.ShowDialog();
        var picked = picker.SelectedChampion;
        if (picked == null)
        {
            return;
        }

        ChangeChampionButton.IsEnabled = false;
        try
        {
            await GameSessionService.Instance.SetChampionManuallyAsync(
                _record, picked.Id, picked.DisplayName);
        }
        finally
        {
            ChangeChampionButton.IsEnabled = true;
        }

        Fill();
    }

    private static System.Windows.Media.ImageSource? LoadImage(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
