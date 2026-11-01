using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Padmon.ViewModels;
using Windows.UI;

namespace Padmon.Controls;

/// <summary>
/// Controller outline in the style of hardwaretester.com. Pressed buttons fill red, the
/// triggers fade from transparent to red with travel, and the sticks move with the input.
/// </summary>
public sealed partial class ControllerDiagram : UserControl
{
    // How far a fully deflected stick travels, in diagram units.
    private const double StickTravel = 30;

    // A stick counts as "in use" once it leaves the resting noise band.
    private const double StickActiveThreshold = 0.06;

    private static readonly SolidColorBrush PressedBrush = new(Color.FromArgb(255, 0xE5, 0x39, 0x35));
    private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

    public ControllerDiagram()
    {
        InitializeComponent();
    }

    public void Update(PadInputState pad)
    {
        var buttons = pad.Buttons;

        SetPressed(FaceA, buttons[0]);
        SetPressed(FaceB, buttons[1]);
        SetPressed(FaceX, buttons[2]);
        SetPressed(FaceY, buttons[3]);
        SetPressed(LeftBumper, buttons[PadInputState.SlotLeftBumper]);
        SetPressed(RightBumper, buttons[PadInputState.SlotRightBumper]);
        SetPressed(BackButton, buttons[PadInputState.SlotBack]);
        SetPressed(StartButton, buttons[PadInputState.SlotStart]);
        SetPressed(DPadUp, buttons[PadInputState.SlotDPadUp]);
        SetPressed(DPadDown, buttons[PadInputState.SlotDPadDown]);
        SetPressed(DPadLeft, buttons[PadInputState.SlotDPadLeft]);
        SetPressed(DPadRight, buttons[PadInputState.SlotDPadRight]);

        LeftTriggerFill.Opacity = Math.Clamp(buttons[PadInputState.SlotLeftTrigger], 0d, 1d);
        RightTriggerFill.Opacity = Math.Clamp(buttons[PadInputState.SlotRightTrigger], 0d, 1d);

        MoveStick(
            LeftKnob,
            LeftKnobMove,
            LeftKnobBackdropMove,
            pad.LeftX,
            pad.LeftY,
            buttons[PadInputState.SlotLeftStick] >= 0.5);
        MoveStick(
            RightKnob,
            RightKnobMove,
            RightKnobBackdropMove,
            pad.RightX,
            pad.RightY,
            buttons[PadInputState.SlotRightStick] >= 0.5);
    }

    private static void SetPressed(Microsoft.UI.Xaml.Shapes.Shape shape, double value)
    {
        shape.Fill = value >= 0.5 ? PressedBrush : ClearBrush;
    }

    private static void MoveStick(
        Microsoft.UI.Xaml.Shapes.Shape knob,
        TranslateTransform move,
        TranslateTransform backdropMove,
        double x,
        double y,
        bool clicked)
    {
        var magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude > 1)
        {
            x /= magnitude;
            y /= magnitude;
            magnitude = 1;
        }

        move.X = x * StickTravel;
        move.Y = y * StickTravel;
        backdropMove.X = move.X;
        backdropMove.Y = move.Y;

        knob.Fill = clicked || magnitude > StickActiveThreshold ? PressedBrush : ClearBrush;
    }
}
