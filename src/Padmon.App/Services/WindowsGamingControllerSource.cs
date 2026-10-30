using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;
using Padmon.Input;
using Windows.Gaming.Input;

namespace Padmon.Services;

/// <summary>
/// Fallback provider for controllers that SDL does not enumerate. Windows
/// exposes Xbox-class pads through Windows.Gaming.Input even when SDL's
/// Windows joystick backends miss them.
/// </summary>
public sealed class WindowsGamingControllerSource : IControllerSource, IAsyncDisposable
{
    private const int DiscoveryDelayMilliseconds = 2_000;
    private const int IdlePollMilliseconds = 500;
    private const int LivePollMilliseconds = 16;

    private readonly object _gate = new();
    private readonly Dictionary<string, RawRuntime> _devices = new(StringComparer.Ordinal);

    private Thread? _thread;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool> _started = NewStartedSource();
    private int _livePreview;
    private bool _disposed;

    public event EventHandler<ControllerConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler<ControllerSnapshotsEventArgs>? SnapshotsAvailable;

    public event EventHandler<string>? ErrorOccurred;

    public IReadOnlyList<ControllerDevice> ConnectedDevices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values.Select(static runtime => runtime.Device).ToArray();
            }
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_thread is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _started = NewStartedSource();
        var token = _cancellation.Token;

        _thread = new Thread(() => Run(token))
        {
            IsBackground = true,
            Name = "padmon-wgi"
        };

        try
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }
        catch (PlatformNotSupportedException)
        {
        }

        _thread.Start();
        await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        var thread = _thread;
        if (thread is null)
        {
            return;
        }

        _thread = null;
        _cancellation?.Cancel();

        await Task.Run(
            () =>
            {
                if (!thread.Join(TimeSpan.FromSeconds(3)))
                {
                    throw new TimeoutException("The Windows.Gaming.Input thread did not stop in time.");
                }
            },
            cancellationToken).ConfigureAwait(false);

        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void SetLivePreview(bool enabled)
    {
        PadmonLog.Info($"WGI live preview: {enabled}");
        Interlocked.Exchange(ref _livePreview, enabled ? 1 : 0);
    }

    public async ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        RawRuntime? runtime;

        lock (_gate)
        {
            runtime = _devices.Values.FirstOrDefault(candidate => string.Equals(
                candidate.Device.StableKey,
                stableKey,
                StringComparison.Ordinal));
        }

        if (runtime is null)
        {
            return false;
        }

        if (runtime.Device.VendorId == 0x045E)
        {
            PadmonLog.Info(
                $"using XInput + Gamepad rumble path for Xbox-class controller (gamepadPresent={runtime.Gamepad is not null})");

            var xinputTask = XInputRumble.PulseAsync(
                lowFrequency,
                highFrequency,
                duration,
                cancellationToken);

            var gamepadTask = runtime.Gamepad is null
                ? Task.FromResult(false)
                : PulseGamepadAsync(
                    runtime.Gamepad,
                    lowFrequency,
                    highFrequency,
                    duration,
                    cancellationToken);

            var results = await Task.WhenAll(xinputTask, gamepadTask).ConfigureAwait(false);
            return results.Any(result => result);
        }

        if (runtime.Gamepad is null)
        {
            return false;
        }

        return await PulseGamepadAsync(
            runtime.Gamepad,
            lowFrequency,
            highFrequency,
            duration,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> PulseGamepadAsync(
        Gamepad gamepad,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            var vibration = new GamepadVibration
            {
                LeftMotor = Math.Clamp(lowFrequency, 0f, 1f),
                RightMotor = Math.Clamp(highFrequency, 0f, 1f)
            };

            var end = DateTimeOffset.UtcNow + duration;

            while (DateTimeOffset.UtcNow < end && !cancellationToken.IsCancellationRequested)
            {
                gamepad.Vibration = vibration;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("Gamepad rumble failed", exception);
            return false;
        }
        finally
        {
            gamepad.Vibration = default;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private void Run(CancellationToken cancellationToken)
    {
        try
        {
            PadmonLog.Info("WGI source starting");

            // WGI's static controller list is often empty immediately after
            // process start, so take one deliberate sampling pass before the
            // app treats the initial scan as complete.
            Thread.Sleep(DiscoveryDelayMilliseconds);
            PollDevices();
            _started.TrySetResult(true);

            while (!cancellationToken.IsCancellationRequested)
            {
                PollDevices();

                if (Volatile.Read(ref _livePreview) != 0)
                {
                    PublishSnapshots();
                    Thread.Sleep(LivePollMilliseconds);
                }
                else
                {
                    Thread.Sleep(IdlePollMilliseconds);
                }
            }
        }
        catch (Exception exception)
        {
            PadmonLog.Error("WGI source failed", exception);
            _started.TrySetException(exception);
            ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            PadmonLog.Info("WGI source stopping");
            CloseAll();
            _started.TrySetResult(false);
        }
    }

    private void PollDevices()
    {
        IReadOnlyList<RawGameController> controllers;
        IReadOnlyList<Gamepad> gamepads;

        try
        {
            controllers = RawGameController.RawGameControllers;
            gamepads = Gamepad.Gamepads;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("WGI enumeration failed", exception);
            return;
        }

        var liveKeys = new HashSet<string>(StringComparer.Ordinal);
        var assignedGamepads = gamepads
            .Where(gamepad => _devices.Values.Any(runtime => ReferenceEquals(runtime.Gamepad, gamepad)))
            .ToHashSet();

        foreach (var controller in controllers)
        {
            var device = Describe(controller);
            if (device is null)
            {
                continue;
            }

            var runtimeKey = !string.IsNullOrWhiteSpace(controller.NonRoamableId)
                ? controller.NonRoamableId.Trim('\0', ' ')
                : device.StableKey;

            liveKeys.Add(runtimeKey);

            bool added;
            lock (_gate)
            {
                added = !_devices.ContainsKey(runtimeKey);

                if (added)
                {
                    Gamepad? gamepad = null;

                    if (device.VendorId == 0x045E)
                    {
                        gamepad = gamepads.FirstOrDefault(candidate => !assignedGamepads.Contains(candidate));
                        if (gamepad is not null)
                        {
                            assignedGamepads.Add(gamepad);
                        }
                    }

                    _devices.Add(runtimeKey, new RawRuntime(controller, gamepad, device, runtimeKey));
                }
                else if (_devices[runtimeKey].Gamepad is null && device.VendorId == 0x045E)
                {
                    var gamepad = gamepads.FirstOrDefault(candidate => !assignedGamepads.Contains(candidate));
                    if (gamepad is not null)
                    {
                        assignedGamepads.Add(gamepad);
                        _devices[runtimeKey].Gamepad = gamepad;
                    }
                }
            }

            if (added)
            {
                PadmonLog.Info(
                    $"WGI controller added: name='{device.Name}' vendor={device.VendorId:X4} product={device.ProductId:X4} key='{device.StableKey}' motors={controller.ForceFeedbackMotors.Count}");
                ConnectionChanged?.Invoke(this, new ControllerConnectionChangedEventArgs(device, true));
            }
        }

        string[] stale;
        lock (_gate)
        {
            stale = _devices.Keys.Where(key => !liveKeys.Contains(key)).ToArray();
        }

        foreach (var key in stale)
        {
            RawRuntime? removed;
            lock (_gate)
            {
                _devices.Remove(key, out removed);
            }

            if (removed is not null)
            {
                PadmonLog.Info($"WGI controller removed: name='{removed.Device.Name}' key='{key}'");
                ConnectionChanged?.Invoke(
                    this,
                    new ControllerConnectionChangedEventArgs(removed.Device, false));
            }
        }
    }

    private void PublishSnapshots()
    {
        RawRuntime[] runtimes;
        lock (_gate)
        {
            runtimes = _devices.Values.ToArray();
        }

        if (runtimes.Length == 0)
        {
            return;
        }

        var snapshots = new List<ControllerSnapshot>(runtimes.Length);

        foreach (var runtime in runtimes)
        {
            try
            {
                snapshots.Add(ReadSnapshot(runtime));
            }
            catch (Exception exception)
            {
                PadmonLog.Error($"WGI reading failed for '{runtime.Device.Name}'", exception);
            }
        }

        if (snapshots.Count > 0)
        {
            SnapshotsAvailable?.Invoke(this, new ControllerSnapshotsEventArgs(snapshots));
        }
    }

    private static ControllerSnapshot ReadSnapshot(RawRuntime runtime)
    {
        if (runtime.Gamepad is not null && runtime.Device.VendorId == 0x045E)
        {
            return ReadGamepadSnapshot(runtime);
        }

        var controller = runtime.Controller;
        var buttons = new bool[(int)controller.ButtonCount];
        var switches = new GameControllerSwitchPosition[(int)controller.SwitchCount];
        var axes = new double[(int)controller.AxisCount];
        controller.GetCurrentReading(buttons, switches, axes);

        var buttonStates = new List<ButtonState>(buttons.Length);
        for (var index = 0; index < buttons.Length; index++)
        {
            buttonStates.Add(new ButtonState(
                index,
                LabelForButton(controller, index),
                buttons[index],
                buttons[index] ? 1f : 0f));
        }

        var axisStates = new List<AxisState>(axes.Length);
        var isXbox = controller.HardwareVendorId == 0x045E;
        for (var index = 0; index < axes.Length; index++)
        {
            var trigger = isXbox && index is 4 or 5;
            var stick = isXbox && index is 0 or 1 or 2 or 3;
            var label = isXbox
                ? index switch
                {
                    0 => "Left X",
                    1 => "Left Y",
                    2 => "Right X",
                    3 => "Right Y",
                    4 => "LT",
                    5 => "RT",
                    _ => $"Axis {index + 1}"
                }
                : $"Axis {index + 1}";

            var value = trigger
                ? Math.Clamp(axes[index], 0d, 1d)
                : stick
                    ? Math.Clamp((axes[index] - 0.5d) * 2d, -1d, 1d)
                    : Math.Clamp(axes[index], -1d, 1d);

            axisStates.Add(new AxisState(
                index,
                label,
                (float)value,
                (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue),
                trigger));
        }

        var hatStates = new List<HatState>(switches.Length);
        for (var index = 0; index < switches.Length; index++)
        {
            hatStates.Add(new HatState(
                index,
                $"Switch {index + 1}",
                (int)switches[index]));
        }

        return new ControllerSnapshot(
            runtime.Device,
            buttonStates,
            axisStates,
            hatStates,
            DateTimeOffset.UtcNow);
    }

    private static ControllerSnapshot ReadGamepadSnapshot(RawRuntime runtime)
    {
        var reading = runtime.Gamepad!.GetCurrentReading();
        var buttons = new List<ButtonState>
        {
            new(0, "A", Has(reading.Buttons, GamepadButtons.A), Has(reading.Buttons, GamepadButtons.A) ? 1f : 0f),
            new(1, "B", Has(reading.Buttons, GamepadButtons.B), Has(reading.Buttons, GamepadButtons.B) ? 1f : 0f),
            new(2, "X", Has(reading.Buttons, GamepadButtons.X), Has(reading.Buttons, GamepadButtons.X) ? 1f : 0f),
            new(3, "Y", Has(reading.Buttons, GamepadButtons.Y), Has(reading.Buttons, GamepadButtons.Y) ? 1f : 0f),
            new(4, "LB", Has(reading.Buttons, GamepadButtons.LeftShoulder), Has(reading.Buttons, GamepadButtons.LeftShoulder) ? 1f : 0f),
            new(5, "RB", Has(reading.Buttons, GamepadButtons.RightShoulder), Has(reading.Buttons, GamepadButtons.RightShoulder) ? 1f : 0f),
            new(6, "View", Has(reading.Buttons, GamepadButtons.View), Has(reading.Buttons, GamepadButtons.View) ? 1f : 0f),
            new(7, "Menu", Has(reading.Buttons, GamepadButtons.Menu), Has(reading.Buttons, GamepadButtons.Menu) ? 1f : 0f),
            new(8, "L3", Has(reading.Buttons, GamepadButtons.LeftThumbstick), Has(reading.Buttons, GamepadButtons.LeftThumbstick) ? 1f : 0f),
            new(9, "R3", Has(reading.Buttons, GamepadButtons.RightThumbstick), Has(reading.Buttons, GamepadButtons.RightThumbstick) ? 1f : 0f),
            new(10, "D-Pad Up", Has(reading.Buttons, GamepadButtons.DPadUp), Has(reading.Buttons, GamepadButtons.DPadUp) ? 1f : 0f),
            new(11, "D-Pad Right", Has(reading.Buttons, GamepadButtons.DPadRight), Has(reading.Buttons, GamepadButtons.DPadRight) ? 1f : 0f),
            new(12, "D-Pad Down", Has(reading.Buttons, GamepadButtons.DPadDown), Has(reading.Buttons, GamepadButtons.DPadDown) ? 1f : 0f),
            new(13, "D-Pad Left", Has(reading.Buttons, GamepadButtons.DPadLeft), Has(reading.Buttons, GamepadButtons.DPadLeft) ? 1f : 0f)
        };

        var axes = new List<AxisState>
        {
            Axis(0, "Left X", reading.LeftThumbstickX, false),
            Axis(1, "Left Y", -reading.LeftThumbstickY, false),
            Axis(2, "Right X", reading.RightThumbstickX, false),
            Axis(3, "Right Y", -reading.RightThumbstickY, false),
            Axis(4, "LT", reading.LeftTrigger, true),
            Axis(5, "RT", reading.RightTrigger, true)
        };

        return new ControllerSnapshot(
            runtime.Device,
            buttons,
            axes,
            [],
            DateTimeOffset.UtcNow);
    }

    private static AxisState Axis(int index, string label, double value, bool trigger)
    {
        var normalized = trigger
            ? Math.Clamp(value, 0d, 1d)
            : Math.Clamp(value, -1d, 1d);

        return new AxisState(
            index,
            label,
            (float)normalized,
            (short)Math.Clamp(normalized * short.MaxValue, short.MinValue, short.MaxValue),
            trigger);
    }

    private static bool Has(GamepadButtons value, GamepadButtons flag) => (value & flag) == flag;

    private static ControllerDevice? Describe(RawGameController controller)
    {
        var vendor = controller.HardwareVendorId;
        var product = controller.HardwareProductId;
        var name = controller.DisplayName;

        if (vendor == 0x045E && product == 0x02FD)
        {
            name = "Xbox Controller";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Controller {vendor:X4}:{product:X4}";
        }

        var key = $"wgi:{vendor:X4}:{product:X4}:{name.Trim().ToLowerInvariant()}";

        return new ControllerDevice(
            key,
            0,
            name.Trim(),
            null,
            vendor,
            product,
            null,
            ControllerKind.Gamepad,
            ConnectionKind.Unknown,
            null,
            PowerState.Unknown,
            false,
            null);
    }

    private static string LabelForButton(RawGameController controller, int index)
    {
        if (controller.HardwareVendorId == 0x045E)
        {
            return index switch
            {
                0 => "A",
                1 => "B",
                2 => "X",
                3 => "Y",
                4 => "LB",
                5 => "RB",
                6 => "Back",
                7 => "Start",
                8 => "L3",
                9 => "R3",
                10 => "D-Pad Up",
                11 => "D-Pad Right",
                12 => "D-Pad Down",
                13 => "D-Pad Left",
                _ => $"Button {index + 1}"
            };
        }

        string label;

        try
        {
            label = controller.GetButtonLabel(index).ToString();
        }
        catch
        {
            return $"Button {index + 1}";
        }

        return label switch
        {
            "XboxA" => "A",
            "XboxB" => "B",
            "XboxX" => "X",
            "XboxY" => "Y",
            "XboxLeftBumper" => "LB",
            "XboxRightBumper" => "RB",
            "XboxBack" => "Back",
            "XboxStart" => "Start",
            "XboxLeftStick" or "XboxLeftThumbstick" => "L3",
            "XboxRightStick" or "XboxRightThumbstick" => "R3",
            "XboxDPadUp" or "XboxUp" => "D-Pad Up",
            "XboxDPadDown" or "XboxDown" => "D-Pad Down",
            "XboxDPadLeft" or "XboxLeft" => "D-Pad Left",
            "XboxDPadRight" or "XboxRight" => "D-Pad Right",
            "XboxGuide" => "Guide",
            "XboxShare" => "Share",
            "Cross" => "Cross",
            "Circle" => "Circle",
            "Square" => "Square",
            "Triangle" => "Triangle",
            "Left1" => "L1",
            "Right1" => "R1",
            "Left2" => "L2",
            "Right2" => "R2",
            "None" => $"Button {index + 1}",
            _ => label
        };
    }

    private void CloseAll()
    {
        lock (_gate)
        {
            _devices.Clear();
        }
    }

    private static TaskCompletionSource<bool> NewStartedSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RawRuntime(
        RawGameController controller,
        Gamepad? gamepad,
        ControllerDevice device,
        string runtimeKey)
    {
        public RawGameController Controller { get; } = controller;

        public Gamepad? Gamepad { get; set; } = gamepad;

        public ControllerDevice Device { get; } = device;

        public string RuntimeKey { get; } = runtimeKey;
    }
}
