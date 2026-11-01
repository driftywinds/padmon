using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Padmon.Core.Devices;

namespace Padmon.ViewModels;

/// <summary>
/// A controller snapshot normalised onto the W3C "standard gamepad" layout
/// (B0..B17 plus two sticks), which is what the diagram tester is drawn against.
/// Sources label buttons differently (SDL, Windows.Gaming.Input, the simulator), so
/// buttons are matched by label rather than by index.
/// </summary>
public sealed class PadInputState
{
    public const int ButtonSlots = 18;

    public const int SlotLeftBumper = 4;
    public const int SlotRightBumper = 5;
    public const int SlotLeftTrigger = 6;
    public const int SlotRightTrigger = 7;
    public const int SlotBack = 8;
    public const int SlotStart = 9;
    public const int SlotLeftStick = 10;
    public const int SlotRightStick = 11;
    public const int SlotDPadUp = 12;
    public const int SlotDPadDown = 13;
    public const int SlotDPadLeft = 14;
    public const int SlotDPadRight = 15;

    /// <summary>0..1 value for each of B0..B17.</summary>
    public double[] Buttons { get; } = new double[ButtonSlots];

    public double LeftX { get; set; }

    public double LeftY { get; set; }

    public double RightX { get; set; }

    public double RightY { get; set; }

    /// <summary>True when the controller exposed recognised gamepad buttons or axes.</summary>
    public bool IsStandard { get; private set; }

    public static PadInputState From(ControllerSnapshot snapshot)
    {
        var state = new PadInputState();
        var matchedButton = false;

        foreach (var button in snapshot.Buttons)
        {
            var slot = SlotForLabel(button.Label);
            if (slot < 0)
            {
                continue;
            }

            matchedButton = true;
            state.Buttons[slot] = Math.Max(state.Buttons[slot], button.Pressed ? Math.Max(button.Value, 1d) : button.Value);
        }

        var matchedAxis = false;
        foreach (var axis in snapshot.Axes)
        {
            switch (axis.Label)
            {
                case "Left X":
                    state.LeftX = axis.Value;
                    matchedAxis = true;
                    break;
                case "Left Y":
                    state.LeftY = axis.Value;
                    matchedAxis = true;
                    break;
                case "Right X":
                    state.RightX = axis.Value;
                    matchedAxis = true;
                    break;
                case "Right Y":
                    state.RightY = axis.Value;
                    matchedAxis = true;
                    break;
                case "LT":
                    state.Buttons[SlotLeftTrigger] = Math.Clamp(axis.Value, 0f, 1f);
                    matchedAxis = true;
                    break;
                case "RT":
                    state.Buttons[SlotRightTrigger] = Math.Clamp(axis.Value, 0f, 1f);
                    matchedAxis = true;
                    break;
            }
        }

        state.IsStandard = matchedButton || matchedAxis;

        if (!state.IsStandard)
        {
            // Raw joystick: no gamepad mapping, so fall back to positional guesses
            // (button N -> B(N-1), first four axes -> sticks, first hat -> D-pad).
            foreach (var button in snapshot.Buttons)
            {
                if (button.Index is >= 0 and < ButtonSlots)
                {
                    state.Buttons[button.Index] = button.Pressed ? 1d : 0d;
                }
            }

            foreach (var axis in snapshot.Axes)
            {
                switch (axis.Index)
                {
                    case 0: state.LeftX = axis.Value; break;
                    case 1: state.LeftY = axis.Value; break;
                    case 2: state.RightX = axis.Value; break;
                    case 3: state.RightY = axis.Value; break;
                }
            }

            if (snapshot.Hats.Count > 0)
            {
                var hat = snapshot.Hats[0].Value;
                state.Buttons[SlotDPadUp] = (hat & 1) != 0 ? 1d : 0d;
                state.Buttons[SlotDPadDown] = (hat & 4) != 0 ? 1d : 0d;
                state.Buttons[SlotDPadLeft] = (hat & 8) != 0 ? 1d : 0d;
                state.Buttons[SlotDPadRight] = (hat & 2) != 0 ? 1d : 0d;
            }
        }

        return state;
    }

    public static string Format(double value)
    {
        // Five decimals like hardwaretester.com; never print "-0.00000".
        if (Math.Abs(value) < 0.000005)
        {
            value = 0;
        }

        return value.ToString("0.00000", CultureInfo.InvariantCulture);
    }

    public static string FormatShort(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static int SlotForLabel(string label) => label switch
    {
        "A" or "Cross" => 0,
        "B" or "Circle" => 1,
        "X" or "Square" => 2,
        "Y" or "Triangle" => 3,
        "LB" => SlotLeftBumper,
        "RB" => SlotRightBumper,
        "LT" => SlotLeftTrigger,
        "RT" => SlotRightTrigger,
        "Back" => SlotBack,
        "Start" => SlotStart,
        "L3" => SlotLeftStick,
        "R3" => SlotRightStick,
        "D-Pad Up" => SlotDPadUp,
        "D-Pad Down" => SlotDPadDown,
        "D-Pad Left" => SlotDPadLeft,
        "D-Pad Right" => SlotDPadRight,
        "Guide" => 16,
        "Touchpad" or "Share" => 17,
        _ => -1
    };
}

/// <summary>One value cell in the tester: a small label, a number, and a level bar.</summary>
public sealed partial class PadCellViewModel : ObservableObject
{
    private const double BarHeight = 34;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _valueText;

    [ObservableProperty]
    private double _fillHeight;

    public PadCellViewModel(string label, string valueText = "0.00")
    {
        _label = label;
        _valueText = valueText;
    }

    public void Set(double value, string text)
    {
        ValueText = text;
        FillHeight = Math.Clamp(Math.Abs(value), 0d, 1d) * BarHeight;
    }
}
