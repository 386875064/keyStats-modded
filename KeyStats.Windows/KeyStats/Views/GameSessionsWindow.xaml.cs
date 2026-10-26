using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using KeyStats.Helpers;
using KeyStats.Services;

namespace KeyStats.Views;

public partial class GameSessionsWindow : Window
{
    public GameSessionsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowBackdrop();
        GameSessionService.Instance.SessionsChanged += OnSessionsChanged;
        Refresh();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        GameSessionService.Instance.SessionsChanged -= OnSessionsChanged;
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnSessionsChanged()
    {
        Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(ApplyWindowBackdrop));
    }

    private void ApplyWindowBackdrop()
    {
        WindowBackdropHelper.Apply(this, NativeInterop.DwmSystemBackdropType.TransientWindow);
    }

    private void Refresh()
    {
        var sessions = GameSessionService.Instance.GetSessions();
        SessionsList.ItemsSource = sessions;
        EmptyHintText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        TotalSessionsText.Text = sessions.Count.ToString("N0");

        var totalSeconds = sessions.Sum(item => item.DurationSeconds);
        var totalKeys = sessions.Sum(item => item.KeyPresses);
        var totalActions = sessions.Sum(item => item.KeyPresses + item.TotalClicks);

        TotalDurationText.Text = totalSeconds >= 3600
            ? $"{totalSeconds / 3600:F1} h"
            : $"{totalSeconds / 60:F0} min";
        TotalKeysText.Text = totalKeys.ToString("N0");
        AverageApmText.Text = totalSeconds > 0
            ? (totalActions / (totalSeconds / 60.0)).ToString("F0")
            : "0";
    }
}
