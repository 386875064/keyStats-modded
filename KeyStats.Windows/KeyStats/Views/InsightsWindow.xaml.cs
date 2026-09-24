using System;
using System.Windows;
using System.Windows.Threading;
using KeyStats.Helpers;
using KeyStats.ViewModels;

namespace KeyStats.Views;

public partial class InsightsWindow : Window
{
    private readonly InsightsViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;

    public InsightsWindow()
    {
        InitializeComponent();
        _viewModel = (InsightsViewModel)DataContext;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
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
        App.CurrentApp?.TrackPageView("insights");
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _viewModel.Refresh();
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
