using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;

namespace Padmon.Services;

public enum NotificationDeliveryKind
{
    InApp,
    Tray
}

public sealed class NotificationService : IDisposable
{
    private Func<string, string, bool>? _trayFallback;
    private Action<string, string>? _inAppFallback;

    public bool PreferNative { get; set; }

    public void SetFallback(
        Func<string, string, bool> trayFallback,
        Action<string, string> inAppFallback)
    {
        _trayFallback = trayFallback;
        _inAppFallback = inAppFallback;
    }

    public void ShowConnected(string displayName, ControllerDevice device)
    {
        var title = $"{displayName} connected";
        var body = Describe(device);

        if (PreferNative && TryShowTray(title, body))
        {
            return;
        }

        PadmonLog.Info($"showing in-app notification: {title}");
        _inAppFallback?.Invoke(title, body);
    }

    public void ShowDisconnected(string displayName, ControllerDevice device)
    {
        var title = $"{displayName} disconnected";
        var body = Describe(device);

        if (PreferNative && TryShowTray(title, body))
        {
            return;
        }

        PadmonLog.Info($"showing in-app notification: {title}");
        _inAppFallback?.Invoke(title, body);
    }

    public NotificationDeliveryKind ShowTest()
    {
        const string title = "padmon notification test";
        const string body = "If you can see this, notifications are working.";

        if (PreferNative && TryShowTray(title, body))
        {
            return NotificationDeliveryKind.Tray;
        }

        _inAppFallback?.Invoke(title, body);
        return NotificationDeliveryKind.InApp;
    }

    public void Dispose()
    {
    }

    private bool TryShowTray(string title, string body)
    {
        if (_trayFallback?.Invoke(title, body) == true)
        {
            PadmonLog.Info($"tray notification shown: {title}");
            return true;
        }

        PadmonLog.Info($"tray notification unavailable: {title}");
        return false;
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
}