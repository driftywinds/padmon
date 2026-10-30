using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Core.Devices;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class ControllerDetailPage : Page
{
    private static readonly ControllerDevice PlaceholderDevice = new(
        "placeholder",
        0,
        "Controller",
        null,
        0,
        0,
        null,
        ControllerKind.Unknown,
        ConnectionKind.Unknown,
        null,
        PowerState.Unknown,
        false,
        null);

    private ControllerViewModel? _controller;

    public ControllerDetailPage()
    {
        Tester = new ControllerTesterViewModel(new ControllerViewModel(PlaceholderDevice));
        InitializeComponent();
    }

    public ControllerTesterViewModel Tester { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not string stableKey)
        {
            return;
        }

        _controller = App.Services.Monitor.Controllers.FirstOrDefault(
            controller => string.Equals(controller.StableKey, stableKey, StringComparison.Ordinal));

        if (_controller is null)
        {
            return;
        }

        Tester = new ControllerTesterViewModel(_controller);
        _controller.PropertyChanged += OnControllerPropertyChanged;
        App.Services.Monitor.ControllerDisconnected += OnControllerDisconnected;

        ControllerName.Text = _controller.DisplayName;
        ControllerSubtitle.Text = _controller.Subtitle;
        BatteryText.Text = _controller.BatteryLabel;
        MappingBadge.Text = _controller.DeviceBadge;

        if (_controller.Snapshot is not null)
        {
            Tester.ApplySnapshot(_controller.Snapshot);
        }

        Bindings.Update();
        UpdateStickVisuals();

        App.Services.Monitor.SetLivePreview(true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        App.Services.Monitor.SetLivePreview(false);

        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnControllerPropertyChanged;
        }

        App.Services.Monitor.ControllerDisconnected -= OnControllerDisconnected;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ControllerViewModel.Snapshot) || _controller?.Snapshot is null)
        {
            return;
        }

        Tester.ApplySnapshot(_controller.Snapshot);
        BatteryText.Text = _controller.BatteryLabel;
        UpdateStickVisuals();
    }

    private void OnControllerDisconnected(object? sender, ControllerViewModel controller)
    {
        if (_controller is null || !string.Equals(controller.StableKey, _controller.StableKey, StringComparison.Ordinal))
        {
            return;
        }

        DisconnectedBar.IsOpen = true;
        App.Services.Monitor.SetLivePreview(false);
    }

    private void UpdateStickVisuals()
    {
        const double radius = 52;

        LeftStickTransform.X = Tester.LeftStickX * radius;
        LeftStickTransform.Y = -Tester.LeftStickY * radius;
        RightStickTransform.X = Tester.RightStickX * radius;
        RightStickTransform.Y = -Tester.RightStickY * radius;
    }

    private async void Rumble_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null)
        {
            return;
        }

        await App.Services.Monitor.RumbleAsync(
            _controller.StableKey,
            0.7f,
            0.7f,
            TimeSpan.FromMilliseconds(350));
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
