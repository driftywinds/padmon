using CommunityToolkit.Mvvm.ComponentModel;
using Padmon.Core.Devices;

namespace Padmon.ViewModels;

public sealed partial class ControllerViewModel : ObservableObject
{
    [ObservableProperty]
    private ControllerDevice _device;

    [ObservableProperty]
    private ControllerSnapshot? _snapshot;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private bool _isNew;

    public ControllerViewModel(ControllerDevice device)
    {
        _device = device;
        _displayName = device.Name;
    }

    public string StableKey => Device.StableKey;

    public string HardwareName => Device.Name;

    public string KindLabel => Device.Kind switch
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

    public string ConnectionLabel => Device.Connection switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.Virtual => "Virtual",
        _ => "Unknown"
    };

    public string Subtitle => $"{KindLabel} · {ConnectionLabel}";

    public string BatteryLabel => Device.Power switch
    {
        PowerState.Charging => Device.BatteryPercent is int charging ? $"Charging · {charging}%" : "Charging",
        PowerState.Charged => "Charged",
        PowerState.OnBattery => Device.BatteryPercent is int battery ? $"{battery}%" : "On battery",
        PowerState.Wired => "USB powered",
        _ => Device.BatteryPercent is int percent ? $"{percent}%" : "Battery unknown"
    };

    public bool HasBattery => Device.BatteryPercent is not null || Device.Power is PowerState.Charging or PowerState.Charged;

    public string DeviceBadge => Device.IsMapped ? "MAPPED" : "RAW INPUT";

    public string PrimaryInputLabel => Snapshot is null
        ? "Open tester for live input"
        : Snapshot.Buttons.FirstOrDefault(button => button.Pressed).Label ?? "No buttons pressed";

    public string Initials
    {
        get
        {
            var words = Device.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return "?";
            }

            return words.Length == 1
                ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                : $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
        }
    }

    public void ApplyDevice(ControllerDevice device)
    {
        Device = device;
    }

    public void ApplySnapshot(ControllerSnapshot snapshot)
    {
        Snapshot = snapshot;
        Device = snapshot.Device;
        OnPropertyChanged(nameof(PrimaryInputLabel));
    }

    public void SetDisplayName(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? HardwareName : displayName.Trim();
    }

    partial void OnDeviceChanged(ControllerDevice value)
    {
        OnPropertyChanged(nameof(StableKey));
        OnPropertyChanged(nameof(HardwareName));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(BatteryLabel));
        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(DeviceBadge));
        OnPropertyChanged(nameof(Initials));
    }
}
