using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KeyStats.Helpers;
using KeyStats.Models;
using KeyStats.ViewModels;

namespace KeyStats.Views;

public partial class GameStatsWindow : Window
{
    private readonly GameStatsViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;

    public GameStatsWindow()
    {
        InitializeComponent();
        _viewModel = (GameStatsViewModel)DataContext;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        _viewModel.Refresh();
        _refreshTimer.Start();
        App.CurrentApp?.TrackPageView("game_stats");
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _viewModel.Refresh();
    }

    private void SessionRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (sender is FrameworkElement element &&
            element.DataContext is GameStatsRowItem row &&
            row.Session != null)
        {
            OpenDetail(row.Session);
        }
    }

    private void OpenDetail(GameSession session)
    {
        var window = new GameSessionDetailWindow(session)
        {
            Owner = this
        };
        window.Show();
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(ApplyWindowBackdrop));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void ApplyWindowBackdrop()
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
    }
}
