using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyStats.Helpers;
using KeyStats.Services;
using Forms = System.Windows.Forms;

namespace KeyStats.Views;

/// <summary>
/// Compact always-on-top HUD shown while a game session is running.
/// Unlike the daily floating stats window, this one is never auto-hidden by
/// the fullscreen detector — it exists precisely for fullscreen games.
/// </summary>
public partial class GameOverlayWindow : Window
{
    private const double EdgeMargin = 20;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _positionSaveTimer;
    private bool _isLoaded;
    private bool _isRestoringPosition;
    private bool _isSessionVisible;

    public GameOverlayWindow()
    {
        InitializeComponent();

        Topmost = true;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTick;

        _positionSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _positionSaveTimer.Tick += OnPositionSaveTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        LocationChanged += OnLocationChanged;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeInterop.HideWindowFromSwitcher(new WindowInteropHelper(this).Handle);
        ApplySurface();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootBorder.ContextMenu = BuildContextMenu();
        RestorePosition();
        _isLoaded = true;
        Refresh();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        _positionSaveTimer.Stop();
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(ApplySurface));
    }

    private void ApplySurface()
    {
        RootBorder.SetResourceReference(Border.BackgroundProperty, "FloatingStatsSurfaceBrush");
        RootBorder.SetResourceReference(Border.BorderBrushProperty, "TrayPopupBorderBrush");
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        var settings = StatsManager.Instance.Settings;
        if (!settings.GameOverlayEnabled)
        {
            HideForSession(false);
            return;
        }

        var snapshot = GameSessionService.Instance.GetSnapshot();
        if (!snapshot.IsSessionActive)
        {
            HideForSession(false);
            return;
        }

        DurationTextBlock.Text = FormatDuration(snapshot.Elapsed);
        ApmTextBlock.Text = FormatNumber(snapshot.RollingApm);
        KeysTextBlock.Text = FormatNumber(snapshot.KeyPresses);
        ClicksTextBlock.Text = FormatNumber(snapshot.TotalClicks);

        DurationTextBlock.ToolTip = KeyStats.Properties.Strings.GameStats_Duration;
        ApmTextBlock.ToolTip = KeyStats.Properties.Strings.GameStats_Apm;
        KeysTextBlock.ToolTip = KeyStats.Properties.Strings.GameStats_Keys;
        ClicksTextBlock.ToolTip = KeyStats.Properties.Strings.GameStats_Clicks;

        HideForSession(true);
    }

    private void HideForSession(bool shouldBeVisible)
    {
        if (shouldBeVisible)
        {
            if (!_isSessionVisible)
            {
                _isSessionVisible = true;
                if (!IsVisible)
                {
                    Show();
                }
            }

            return;
        }

        if (_isSessionVisible)
        {
            _isSessionVisible = false;
            Hide();
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem
        {
            Header = KeyStats.Properties.Strings.Tray_GameStats
        };
        openItem.Click += (_, _) => App.CurrentApp?.ShowGameStatsWindow();
        menu.Items.Add(openItem);

        var lockItem = new MenuItem
        {
            Header = KeyStats.Properties.Strings.FloatingStats_LockPosition,
            IsCheckable = true,
            IsChecked = StatsManager.Instance.Settings.GameOverlayPositionLocked
        };
        lockItem.Click += (_, _) =>
        {
            var settings = StatsManager.Instance.Settings;
            settings.GameOverlayPositionLocked = lockItem.IsChecked;
            StatsManager.Instance.SaveSettings();
            ApplyBehaviorSettings();
        };
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());

        var hideItem = new MenuItem
        {
            Header = KeyStats.Properties.Strings.FloatingStats_Hide
        };
        hideItem.Click += (_, _) => App.CurrentApp?.SetGameOverlayVisible(false);
        menu.Items.Add(hideItem);

        menu.Opened += (_, _) =>
        {
            lockItem.IsChecked = StatsManager.Instance.Settings.GameOverlayPositionLocked;
        };

        return menu;
    }

    public void ApplyBehaviorSettings()
    {
        Topmost = true;
        RootBorder.Cursor = StatsManager.Instance.Settings.GameOverlayPositionLocked
            ? Cursors.Arrow
            : Cursors.SizeAll;
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            App.CurrentApp?.ShowGameStatsWindow();
            e.Handled = true;
            return;
        }

        if (StatsManager.Instance.Settings.GameOverlayPositionLocked)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse released before WPF entered the native drag loop.
        }
        finally
        {
            ClampToVirtualScreen();
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_isLoaded || _isRestoringPosition)
        {
            return;
        }

        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    private void OnPositionSaveTick(object? sender, EventArgs e)
    {
        _positionSaveTimer.Stop();
        SavePosition();
    }

    private void SavePosition()
    {
        var settings = StatsManager.Instance.Settings;
        settings.GameOverlayLeft = Left;
        settings.GameOverlayTop = Top;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            settings.GameOverlayMonitorDeviceName = Forms.Screen.FromHandle(handle).DeviceName;
        }

        StatsManager.Instance.SaveSettings();
    }

    private void RestorePosition()
    {
        var settings = StatsManager.Instance.Settings;
        var workArea = GetTargetWorkArea(settings.GameOverlayMonitorDeviceName);

        var left = settings.GameOverlayLeft ?? (workArea.Right - Width - EdgeMargin);
        var top = settings.GameOverlayTop ?? (workArea.Top + 96);

        _isRestoringPosition = true;
        try
        {
            Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - Width));
            Top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - Height));
        }
        finally
        {
            _isRestoringPosition = false;
        }
    }

    private void ClampToVirtualScreen()
    {
        var workArea = GetTargetWorkArea(null);
        var left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        var top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        if (Math.Abs(left - Left) < 0.5 && Math.Abs(top - Top) < 0.5)
        {
            SavePosition();
            return;
        }

        _isRestoringPosition = true;
        try
        {
            Left = left;
            Top = top;
        }
        finally
        {
            _isRestoringPosition = false;
        }

        SavePosition();
    }

    private Rect GetTargetWorkArea(string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            foreach (var screen in Forms.Screen.AllScreens)
            {
                if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return ToDeviceIndependentRect(screen);
                }
            }
        }

        foreach (var screen in Forms.Screen.AllScreens)
        {
            if (screen.Primary)
            {
                return ToDeviceIndependentRect(screen);
            }
        }

        return SystemParameters.WorkArea;
    }

    private Rect ToDeviceIndependentRect(Forms.Screen screen)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        return MonitorGeometryHelper.GetWorkingAreaInDips(screen, transform);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var totalHours = (int)span.TotalHours;
        return totalHours > 0
            ? $"{totalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return "0";
        }

        var rounded = Math.Round(value);
        if (rounded >= 100000)
        {
            return $"{rounded / 1000.0:F0}k";
        }

        return rounded.ToString("N0");
    }
}
