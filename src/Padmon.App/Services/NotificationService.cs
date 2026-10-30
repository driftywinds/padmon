using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;

namespace Padmon.Services;

public sealed class NotificationService : IDisposable
{
    private bool _registered;
    private bool _preferNative;
    private Action<string, string>? _fallback;

    public bool PreferNative
    {
        get => _preferNative;
        set
        {
            _preferNative = value;

            if (value)
            {
                Initialize();
            }
        }
    }

    public void Initialize()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
            PadmonLog.Info("app notification manager registered");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"padmon notifications are unavailable: {exception}");
            PadmonLog.Error("app notification manager unavailable; using tray fallback", exception);
            LogFailure(exception);
        }
    }

    private static void LogFailure(Exception exception)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "padmon-notification-error.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void SetFallback(Action<string, string> fallback)
    {
        _fallback = fallback;
    }

    public void ShowConnected(string displayName, ControllerDevice device)
    {
        var title = $"{displayName} connected";
        var body = $"{Describe(device)} · padmon is watching";

        if (PreferNative && TryShowNative(title, body))
        {
            return;
        }

        PadmonLog.Info($"notification fallback: {title}");
        _fallback?.Invoke(title, body);
    }

    public void ShowDisconnected(string displayName, ControllerDevice device)
    {
        var title = $"{displayName} disconnected";
        var body = $"{Describe(device)} · still watching";

        if (PreferNative && TryShowNative(title, body))
        {
            return;
        }

        PadmonLog.Info($"notification fallback: {title}");
        _fallback?.Invoke(title, body);
    }

    public bool ShowTest()
    {
        const string title = "padmon notification test";
        const string body = "If you can see this, notifications are working.";

        if (PreferNative && TryShowNative(title, body))
        {
            return true;
        }

        _fallback?.Invoke(title, body);
        return false;
    }

    private bool TryShowNative(string title, string body)
    {
        if (TryShow(title, body))
        {
            return true;
        }

        PadmonLog.Info("modern notification path unavailable; trying desktop toast path");
        return DesktopToastService.TryShow(title, body);
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"padmon notification cleanup failed: {exception}");
        }

        _registered = false;
    }

    private bool TryShow(string title, string body)
    {
        if (!_registered)
        {
            return false;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(body)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"padmon notification failed: {exception}");
            return false;
        }
    }

    private static string Describe(ControllerDevice device)
    {
        var kind = device.Kind switch
        {
            ControllerKind.Gamepad => "Gamepad",
            ControllerKind.Joystick => "Joystick",
            ControllerKind.Wheel => "Wheel",
            ControllerKind.ArcadeStick => "Arcade stick",
            ControllerKind.FlightStick => "Flight stick",
            ControllerKind.DancePad => "Dance pad",
            ControllerKind.Guitar => "Guitar",
            ControllerKind.DrumKit => "Drum kit",
            ControllerKind.ArcadePad => "Arcade pad",
            ControllerKind.Throttle => "Throttle",
            _ => "Controller"
        };

        var connection = device.Connection switch
        {
            ConnectionKind.Usb => "USB",
            ConnectionKind.Bluetooth => "Bluetooth",
            ConnectionKind.Virtual => "Virtual",
            _ => null
        };

        return connection is null ? kind : $"{kind} · {connection}";
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (App.MainWindow is null)
        {
            return;
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.AppWindow.Show();
            App.MainWindow.Activate();
        });
    }
}
