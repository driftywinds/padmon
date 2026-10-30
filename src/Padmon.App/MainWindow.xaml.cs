using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Padmon.Services;
using Padmon.Views;
using Windows.Graphics;

namespace Padmon;

public sealed partial class MainWindow : Window, IDisposable
{
    private TrayIconService? _trayIcon;
    private bool _trayInitialized;
    private bool _isExiting;

    public bool IsWindowActive { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Title = "padmon";
        AppWindow.Resize(new SizeInt32(1180, 760));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentFrame.Navigate(typeof(ControllersPage));

        Activated += OnActivated;
        Activated += OnWindowActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        App.Services.Monitor.Controllers.CollectionChanged += OnControllersChanged;
        App.Services.Monitor.PropertyChanged += OnMonitorPropertyChanged;
        UpdateMonitoringStatus();
    }

    public void NavigateTo(string tag)
    {
        var item = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));

        if (item is not null && !ReferenceEquals(Navigation.SelectedItem, item))
        {
            Navigation.SelectedItem = item;
        }

        NavigatePage(tag);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        IsWindowActive = true;
    }

    public void HideToTray()
    {
        IsWindowActive = false;
        AppWindow.Hide();
    }

    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_trayInitialized)
        {
            return;
        }

        _trayInitialized = true;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _trayIcon = new TrayIconService(
            hwnd,
            ShowFromTray,
            () =>
            {
                ShowFromTray();
                NavigateTo("settings");
            },
            ExitFromTray);

        _trayIcon.Create();
        App.Services.Notifications.SetFallback(_trayIcon.ShowBalloon);
        UpdateTrayToolTip();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.Services.Monitor.Controllers.CollectionChanged -= OnControllersChanged;
        App.Services.Monitor.PropertyChanged -= OnMonitorPropertyChanged;
        Dispose();

        try
        {
            App.Services.Monitor.StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting || !App.Services.Settings.BackgroundMonitoring)
        {
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void OnControllersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateTrayToolTip();
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ControllerMonitor.IsMonitoring) or nameof(ControllerMonitor.Error))
        {
            UpdateMonitoringStatus();
        }
    }

    private void UpdateMonitoringStatus()
    {
        if (!string.IsNullOrWhiteSpace(App.Services.Monitor.Error))
        {
            MonitorStatusText.Text = "Input issue";
            MonitorDot.Fill = new SolidColorBrush(Colors.IndianRed);
            return;
        }

        if (App.Services.Monitor.IsMonitoring)
        {
            MonitorStatusText.Text = "Watching";
            MonitorDot.Fill = new SolidColorBrush(Colors.SeaGreen);
            return;
        }

        MonitorStatusText.Text = "Starting";
        MonitorDot.Fill = new SolidColorBrush(Colors.Goldenrod);
    }

    private void UpdateTrayToolTip()
    {
        if (_trayIcon is null)
        {
            return;
        }

        var count = App.Services.Monitor.ConnectedCount;
        _trayIcon.UpdateTooltip(count switch
        {
            0 => "padmon — watching",
            1 => "padmon — 1 controller connected",
            _ => $"padmon — {count} controllers connected"
        });
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        IsWindowActive = true;
    }

    private async void ExitFromTray()
    {
        _isExiting = true;

        try
        {
            await App.Services.Monitor.StopAsync();
        }
        catch
        {
        }

        App.Services.Notifications.Dispose();
        AppWindow.Hide();
        Close();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            NavigatePage(tag);
        }
    }

    private void NavigatePage(string tag)
    {
        var pageType = tag switch
        {
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ControllersPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
