namespace Padmon.Core.Devices;

public enum ControllerKind
{
    Unknown,
    Gamepad,
    Joystick,
    Wheel,
    ArcadeStick,
    FlightStick,
    DancePad,
    Guitar,
    DrumKit,
    ArcadePad,
    Throttle,
    Other
}

public enum ConnectionKind
{
    Unknown,
    Usb,
    Bluetooth,
    Virtual
}

public enum PowerState
{
    Unknown,
    Wired,
    OnBattery,
    Charging,
    Charged
}

public sealed record ControllerDevice(
    string StableKey,
    uint InstanceId,
    string Name,
    string? Serial,
    ushort VendorId,
    ushort ProductId,
    string? SdlGuid,
    ControllerKind Kind,
    ConnectionKind Connection,
    int? BatteryPercent,
    PowerState Power,
    bool IsMapped,
    string? Path);

public readonly record struct ButtonState(int Index, string Label, bool Pressed, float Value);

public readonly record struct AxisState(int Index, string Label, float Value, short RawValue, bool IsTrigger);

public readonly record struct HatState(int Index, string Label, int Value);

public sealed record ControllerSnapshot(
    ControllerDevice Device,
    IReadOnlyList<ButtonState> Buttons,
    IReadOnlyList<AxisState> Axes,
    IReadOnlyList<HatState> Hats,
    DateTimeOffset ObservedAt);
