using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KeyStats.Helpers;
using KeyStats.Models;
using KeyStats.ViewModels;
using KeyStats.Views.Controls;

namespace KeyStats.Views;

public partial class GameSessionDetailWindow : Window
{
    private readonly GameSessionDetailViewModel _viewModel;

    public GameSessionDetailWindow(GameSession session)
    {
        InitializeComponent();
        _viewModel = new GameSessionDetailViewModel(session);
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        ApplyHeatmap();
        App.CurrentApp?.TrackPageView("game_session_detail");
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyWindowBackdrop();
            ApplyHeatmap();
        }));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void ApplyHeatmap()
    {
        var counts = _viewModel.HeatmapCounts;
        var visible = counts
            .Where(kv => KeyboardHeatmapControl.SupportedKeyIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        KeyboardHeatmapView.Apply(visible);
        KeyboardHeatmapView.InvalidateVisual();
    }

    private void ApplyWindowBackdrop()
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
    }
}
