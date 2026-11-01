using Padmon.Core.Devices;

namespace Padmon.Services;

/// <summary>
/// Builds animated sample input for the tester's preview mode, which is opened from
/// Settings when no controller is connected. Every button is pressed in turn, the sticks
/// sweep and the triggers ramp, so the whole tester UI can be exercised without hardware.
/// </summary>
internal static class PreviewSnapshotFactory
{
    private static readonly string[] ButtonLabels =
    [
        "A", "B", "X", "Y", "LB", "RB", "Back", "Start", "L3", "R3",
        "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right", "Guide"
    ];

    // Seconds each button stays pressed; a few empty steps pad the loop.
    private const double StepSeconds = 0.6;
    private const int EmptySteps = 4;

    public static ControllerDevice Device { get; } = new(
        "preview:demo",
        0,
        "Preview controller",
        null,
        0,
        0,
        null,
        ControllerKind.Gamepad,
        ConnectionKind.Virtual,
        100,
        PowerState.Wired,
        true,
        null);

    public static ControllerSnapshot Create(double seconds)
    {
        var step = (int)(seconds / StepSeconds) % (ButtonLabels.Length + EmptySteps);

        var buttons = new List<ButtonState>(ButtonLabels.Length);
        for (var index = 0; index < ButtonLabels.Length; index++)
        {
            var pressed = index == step;
            buttons.Add(new ButtonState(index, ButtonLabels[index], pressed, pressed ? 1f : 0f));
        }

        var leftReach = 0.6 + (0.4 * Math.Sin(seconds * 0.7));
        var leftX = Math.Sin(seconds * 1.2) * leftReach;
        var leftY = Math.Cos(seconds * 1.2) * leftReach;
        var rightX = Math.Sin(seconds * 0.8) * 0.9;
        var rightY = Math.Cos(seconds * 1.3) * 0.6;
        var leftTrigger = (Math.Sin(seconds * 0.9) + 1) / 2;
        var rightTrigger = (Math.Sin((seconds * 0.6) + 1.4) + 1) / 2;

        var axes = new List<AxisState>
        {
            Axis(0, "Left X", leftX, false),
            Axis(1, "Left Y", leftY, false),
            Axis(2, "Right X", rightX, false),
            Axis(3, "Right Y", rightY, false),
            Axis(4, "LT", leftTrigger, true),
            Axis(5, "RT", rightTrigger, true)
        };

        return new ControllerSnapshot(Device, buttons, axes, [], DateTimeOffset.UtcNow);
    }

    private static AxisState Axis(int index, string label, double value, bool trigger)
    {
        var normalized = (float)value;
        return new AxisState(index, label, normalized, (short)(normalized * short.MaxValue), trigger);
    }
}
