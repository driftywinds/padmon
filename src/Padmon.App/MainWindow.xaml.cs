using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Padmon.Core.Diagnostics;
using Padmon.Services;
using Padmon.Views;
using Windows.Graphics;

namespace Padmon;

public sealed partial class MainWindow : Window, IDisposable
{
    private TrayIconService? _trayIcon;
    private NotificationWindow? _notificationWindow;
    private bool _trayInitialized;
    private bool _isExiting;

    public bool IsWindowActive { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, args) =>
        {
            App.Services.Couch.SetPadmonActive(
                args.WindowActivationState != WindowActivationState.Deactivated);
        };
        BuildInfoText.Text = $"{UpdateService.CurrentVersion} · SDL {SdlInfo.Version}";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Title = "padmon";
        AppWindow.Resize(new SizeInt32(1180, 760));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "padmon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
        }

        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentFrame.Navigate(typeof(ControllersPage));

        if (AppEnvironment.IsPackaged)
        {
            CouchNavItem.Visibility = Visibility.Collapsed;
        }

        Activated += OnActivated;
        Activated += OnWindowActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        App.Services.Monitor.Controllers.CollectionChanged += OnControllersChanged;
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
        ThemeService.Apply(RootGrid, theme, App.Services.Settings);
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
        App.Services.Notifications.SetFallback(
            _trayIcon.ShowBalloon,
            ShowInAppNotification);
        UpdateTrayToolTip();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.Services.Monitor.Controllers.CollectionChanged -= OnControllersChanged;
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

        App.Services.Couch.Dispose();
        _notificationWindow?.Close();
        _notificationWindow = null;
    }

    private void ShowInAppNotification(string title, string body)
    {
        _notificationWindow ??= new NotificationWindow();
        _notificationWindow.ShowMessage(title, body);
    }

    public NotificationWindow ShowNotificationPlacement()
    {
        _notificationWindow ??= new NotificationWindow();
        _notificationWindow.ShowForPlacement();
        return _notificationWindow;
    }

    public void HideNotificationPlacement()
    {
        _notificationWindow?.HideForPlacement();
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

    private void FooterSettings_Click(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = null;
        NavigatePage("settings");
    }

    private void FooterUpdate_Click(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = null;
        NavigatePage("update");
    }

    private void NavigatePage(string tag)
    {
        var pageType = tag switch
        {
            "couch" => typeof(CouchControlPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            "update" => typeof(UpdatePage),
            _ => typeof(ControllersPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            try
            {
                ContentFrame.Navigate(pageType);
            }
            catch (Exception exception)
            {
                PadmonLog.Error($"navigation to '{tag}' failed", exception);
            }
        }
    }
}
