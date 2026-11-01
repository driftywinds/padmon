using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Padmon.Core.Devices;
using Windows.UI;

namespace Padmon.Views;

/// <summary>
/// A general-purpose gamepad diagram used by the input tester. Pressed
/// buttons fill red, the triggers fade from transparent to red with travel,
/// and the stick wells act as live position graphs.
/// </summary>
public sealed partial class ControllerDiagram : UserControl
{
    /// <summary>Pixels a stick dot travels from the centre at full deflection.</summary>
    private const double StickTravel = 36;

    private const double StickCentre = 52;

    private static readonly Color PressedColor = Color.FromArgb(255, 0xE8, 0x11, 0x23);

    private readonly List<DiagramButton> _buttons = [];
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly SolidColorBrush _pressedBrush = new(PressedColor);
    private readonly SolidColorBrush _pressedLabelBrush = new(Color.FromArgb(255, 255, 255, 255));
    private readonly SolidColorBrush _leftTriggerBrush = new(PressedColor);
    private readonly SolidColorBrush _rightTriggerBrush = new(PressedColor);
    private Brush? _triggerIdleStroke;

    public ControllerDiagram()
    {
        InitializeComponent();

        _triggerIdleStroke = LtTrigger.BorderBrush;
        LtTrigger.Background = _leftTriggerBrush;
        RtTrigger.Background = _rightTriggerBrush;

        RegisterShape("Guide", GuideButton);
        RegisterShape("View", ViewButton);
        RegisterShape("Menu", MenuButton);
        RegisterShape("DPadUp", DPadUp);
        RegisterShape("DPadDown", DPadDown);
        RegisterShape("DPadLeft", DPadLeft);
        RegisterShape("DPadRight", DPadRight);
        RegisterShape("A", FaceA, FaceALabel);
        RegisterShape("B", FaceB, FaceBLabel);
        RegisterShape("X", FaceX, FaceXLabel);
        RegisterShape("Y", FaceY, FaceYLabel);
        RegisterShape("L3", LeftStickWell);
        RegisterShape("R3", RightStickWell);
        RegisterBorder("LB", LbBumper);
        RegisterBorder("RB", RbBumper);
    }

    /// <summary>
    /// Pushes one input snapshot into the diagram. Called on the UI thread for
    /// every sample the monitor publishes.
    /// </summary>
    public void ApplySnapshot(ControllerSnapshot snapshot)
    {
        _active.Clear();

        foreach (var button in snapshot.Buttons)
        {
            if (!button.Pressed)
            {
                continue;
            }

            var key = ResolveButtonKey(button);
            if (key is not null)
            {
                _active.Add(key);
            }
        }

        var leftTrigger = Math.Clamp(ReadAxis(snapshot.Axes, "LT", 4), 0f, 1f);
        var rightTrigger = Math.Clamp(ReadAxis(snapshot.Axes, "RT", 5), 0f, 1f);

        ApplyTrigger(LtTrigger, _leftTriggerBrush, leftTrigger);
        ApplyTrigger(RtTrigger, _rightTriggerBrush, rightTrigger);

        var leftX = ReadAxis(snapshot.Axes, "Left X", 0);
        var leftY = ReadAxis(snapshot.Axes, "Left Y", 1);
        var rightX = ReadAxis(snapshot.Axes, "Right X", 2);
        var rightY = ReadAxis(snapshot.Axes, "Right Y", 3);

        MoveStick(LeftStickTransform, LeftStickRay, leftX, leftY);
        MoveStick(RightStickTransform, RightStickRay, rightX, rightY);

        FaceALabel.Text = FaceGlyph(snapshot.Buttons, 0, "A");
        FaceBLabel.Text = FaceGlyph(snapshot.Buttons, 1, "B");
        FaceXLabel.Text = FaceGlyph(snapshot.Buttons, 2, "X");
        FaceYLabel.Text = FaceGlyph(snapshot.Buttons, 3, "Y");

        foreach (var button in _buttons)
        {
            ApplyButton(button, _active.Contains(button.Key));
        }
    }

    private void RegisterShape(string key, Shape shape, TextBlock? label = null)
    {
        _buttons.Add(new DiagramButton
        {
            Key = key,
            Shape = shape,
            Label = label,
            IdleFill = shape.Fill,
            IdleStroke = shape.Stroke,
            IdleLabel = label?.Foreground
        });
    }

    private void RegisterBorder(string key, Border border)
    {
        _buttons.Add(new DiagramButton
        {
            Key = key,
            Border = border,
            IdleFill = border.Background,
            IdleStroke = border.BorderBrush
        });
    }

    private void ApplyButton(DiagramButton button, bool pressed)
    {
        if (button.Pressed == pressed)
        {
            return;
        }

        button.Pressed = pressed;

        if (button.Shape is not null)
        {
            button.Shape.Fill = pressed ? _pressedBrush : button.IdleFill;
            button.Shape.Stroke = pressed ? _pressedBrush : button.IdleStroke;

            if (button.Label is not null)
            {
                button.Label.Foreground = pressed ? _pressedLabelBrush : button.IdleLabel;
            }

            return;
        }

        if (button.Border is not null)
        {
            button.Border.Background = pressed ? _pressedBrush : button.IdleFill;
            button.Border.BorderBrush = pressed ? _pressedBrush : button.IdleStroke;
        }
    }

    private void ApplyTrigger(Border trigger, SolidColorBrush brush, float value)
    {
        // Nothing at rest, solid red at full travel.
        brush.Opacity = value;
        trigger.BorderBrush = value > 0.02f ? _pressedBrush : _triggerIdleStroke;
    }

    private static void MoveStick(TranslateTransform transform, Line ray, float x, float y)
    {
        var offsetX = Math.Clamp(x, -1f, 1f) * StickTravel;
        var offsetY = Math.Clamp(y, -1f, 1f) * StickTravel;

        transform.X = offsetX;
        transform.Y = offsetY;

        ray.X2 = StickCentre + offsetX;
        ray.Y2 = StickCentre + offsetY;
    }

    private static string? ResolveButtonKey(ButtonState button)
    {
        var key = button.Label switch
        {
            "A" or "Cross" => "A",
            "B" or "Circle" => "B",
            "X" or "Square" => "X",
            "Y" or "Triangle" => "Y",
            "LB" or "Left Shoulder" => "LB",
            "RB" or "Right Shoulder" => "RB",
            "Back" or "View" or "Select" => "View",
            "Start" or "Menu" or "Options" => "Menu",
            "Guide" or "Home" or "PS" => "Guide",
            "L3" => "L3",
            "R3" => "R3",
            "D-Pad Up" => "DPadUp",
            "D-Pad Down" => "DPadDown",
            "D-Pad Left" => "DPadLeft",
            "D-Pad Right" => "DPadRight",
            _ => null
        };

        if (key is not null)
        {
            return key;
        }

        // Raw joysticks report labels like "Button 1"; fall back to the SDL
        // gamepad button order so unnamed pads still light up the diagram.
        return button.Index switch
        {
            0 => "A",
            1 => "B",
            2 => "X",
            3 => "Y",
            4 => "View",
            5 => "Guide",
            6 => "Menu",
            7 => "L3",
            8 => "R3",
            9 => "LB",
            10 => "RB",
            11 => "DPadUp",
            12 => "DPadDown",
            13 => "DPadLeft",
            14 => "DPadRight",
            _ => null
        };
    }

    private static string FaceGlyph(IReadOnlyList<ButtonState> buttons, int index, string fallback)
    {
        foreach (var button in buttons)
        {
            if (button.Index != index)
            {
                continue;
            }

            return button.Label switch
            {
                "Cross" => "✕",
                "Circle" => "○",
                "Square" => "□",
                "Triangle" => "△",
                "A" => "A",
                "B" => "B",
                "X" => "X",
                "Y" => "Y",
                _ => fallback
            };
        }

        return fallback;
    }

    private static float ReadAxis(IReadOnlyList<AxisState> axes, string label, int index)
    {
        foreach (var axis in axes)
        {
            if (string.Equals(axis.Label, label, StringComparison.Ordinal))
            {
                return axis.Value;
            }
        }

        foreach (var axis in axes)
        {
            if (axis.Index == index)
            {
                return axis.Value;
            }
        }

        return 0f;
    }

    private sealed class DiagramButton
    {
        public required string Key { get; init; }

        public Shape? Shape { get; init; }

        public Border? Border { get; init; }

        public TextBlock? Label { get; init; }

        public Brush? IdleFill { get; init; }

        public Brush? IdleStroke { get; init; }

        public Brush? IdleLabel { get; init; }

        public bool Pressed { get; set; }
    }
}
