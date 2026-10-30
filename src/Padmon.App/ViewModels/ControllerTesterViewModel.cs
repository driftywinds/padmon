using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Padmon.Core.Devices;

namespace Padmon.ViewModels;

public sealed partial class ControllerTesterViewModel : ObservableObject
{
    private readonly ControllerViewModel _controller;

    [ObservableProperty]
    private double _leftStickX;

    [ObservableProperty]
    private double _leftStickY;

    [ObservableProperty]
    private double _rightStickX;

    [ObservableProperty]
    private double _rightStickY;

    [ObservableProperty]
    private double _leftTriggerPercent;

    [ObservableProperty]
    private double _rightTriggerPercent;

    [ObservableProperty]
    private string _leftStickText = "0.00, 0.00";

    [ObservableProperty]
    private string _rightStickText = "0.00, 0.00";

    [ObservableProperty]
    private string _leftTriggerText = "0%";

    [ObservableProperty]
    private string _rightTriggerText = "0%";

    public ControllerTesterViewModel(ControllerViewModel controller)
    {
        _controller = controller;
    }

    public ObservableCollection<ButtonStateViewModel> Buttons { get; } = [];

    public ObservableCollection<AxisStateViewModel> Axes { get; } = [];

    public ObservableCollection<HatStateViewModel> Hats { get; } = [];

    public string StableKey => _controller.StableKey;

    public string DisplayName => _controller.DisplayName;

    public string HardwareName => _controller.HardwareName;

    public string Subtitle => _controller.Subtitle;

    public string KindLabel => _controller.KindLabel;

    public string ConnectionLabel => _controller.ConnectionLabel;

    public string BatteryLabel => _controller.BatteryLabel;

    public bool HasHats => Hats.Count > 0;

    public bool IsMapped => _controller.Device.IsMapped;

    public void ApplySnapshot(ControllerSnapshot snapshot)
    {
        SyncButtons(snapshot.Buttons);
        SyncAxes(snapshot.Axes);
        SyncHats(snapshot.Hats);

        var leftX = FindAxis(snapshot.Axes, "Left X");
        var leftY = FindAxis(snapshot.Axes, "Left Y");
        var rightX = FindAxis(snapshot.Axes, "Right X");
        var rightY = FindAxis(snapshot.Axes, "Right Y");
        var leftTrigger = FindAxis(snapshot.Axes, "LT");
        var rightTrigger = FindAxis(snapshot.Axes, "RT");

        LeftStickX = leftX?.Value ?? 0;
        LeftStickY = leftY?.Value ?? 0;
        RightStickX = rightX?.Value ?? 0;
        RightStickY = rightY?.Value ?? 0;
        LeftTriggerPercent = Math.Round((leftTrigger?.Value ?? 0) * 100);
        RightTriggerPercent = Math.Round((rightTrigger?.Value ?? 0) * 100);

        LeftStickText = $"{LeftStickX:+0.00;-0.00;0.00}, {LeftStickY:+0.00;-0.00;0.00}";
        RightStickText = $"{RightStickX:+0.00;-0.00;0.00}, {RightStickY:+0.00;-0.00;0.00}";
        LeftTriggerText = $"{LeftTriggerPercent:0}%";
        RightTriggerText = $"{RightTriggerPercent:0}%";
    }

    private void SyncButtons(IReadOnlyList<ButtonState> buttons)
    {
        if (Buttons.Count != buttons.Count)
        {
            Buttons.Clear();
            foreach (var button in buttons)
            {
                Buttons.Add(new ButtonStateViewModel(button));
            }

            return;
        }

        for (var index = 0; index < buttons.Count; index++)
        {
            Buttons[index].Update(buttons[index]);
        }
    }

    private void SyncAxes(IReadOnlyList<AxisState> axes)
    {
        if (Axes.Count != axes.Count)
        {
            Axes.Clear();
            foreach (var axis in axes)
            {
                Axes.Add(new AxisStateViewModel(axis));
            }

            return;
        }

        for (var index = 0; index < axes.Count; index++)
        {
            Axes[index].Update(axes[index]);
        }
    }

    private void SyncHats(IReadOnlyList<HatState> hats)
    {
        if (Hats.Count != hats.Count)
        {
            Hats.Clear();
            foreach (var hat in hats)
            {
                Hats.Add(new HatStateViewModel(hat));
            }

            OnPropertyChanged(nameof(HasHats));
            return;
        }

        for (var index = 0; index < hats.Count; index++)
        {
            Hats[index].Update(hats[index]);
        }
    }

    private static AxisState? FindAxis(IReadOnlyList<AxisState> axes, string label)
    {
        foreach (var axis in axes)
        {
            if (string.Equals(axis.Label, label, StringComparison.Ordinal))
            {
                return axis;
            }
        }

        return null;
    }
}

public sealed partial class ButtonStateViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isPressed;

    [ObservableProperty]
    private string _label;

    public ButtonStateViewModel(ButtonState state)
    {
        _label = state.Label;
        _isPressed = state.Pressed;
    }

    public void Update(ButtonState state)
    {
        Label = state.Label;
        IsPressed = state.Pressed;
    }
}

public sealed partial class AxisStateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _valueText;

    public AxisStateViewModel(AxisState state)
    {
        _label = state.Label;
        _valueText = Format(state);
    }

    public void Update(AxisState state)
    {
        Label = state.Label;
        ValueText = Format(state);
    }

    private static string Format(AxisState state)
    {
        var normalized = state.IsTrigger
            ? $"{state.Value * 100:0}%"
            : $"{state.Value:+0.00;-0.00;0.00}";

        return $"{normalized}  ({state.RawValue})";
    }
}

public sealed partial class HatStateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _valueText;

    public HatStateViewModel(HatState state)
    {
        _label = state.Label;
        _valueText = Describe(state.Value);
    }

    public void Update(HatState state)
    {
        Label = state.Label;
        ValueText = Describe(state.Value);
    }

    private static string Describe(int value) => value switch
    {
        0 => "Centered",
        1 => "Up",
        2 => "Right",
        3 => "Up + right",
        4 => "Down",
        6 => "Down + right",
        8 => "Left",
        9 => "Up + left",
        12 => "Down + left",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };
}
