using System.Text;
using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;
using SDL3;

namespace Padmon.Input;

/// <summary>
/// SDL3-backed controller source. SDL is initialized and pumped on one
/// dedicated background thread; all public state is copied before it leaves
/// that thread.
/// </summary>
public sealed class SdlControllerSource : IControllerSource
{
    private const int LivePollMilliseconds = 4;
    private const int IdlePollMilliseconds = 250;
    private const int LivePublishMilliseconds = 16;
    private const int ReconcileMilliseconds = 2_000;

    private static readonly bool Diagnostic =
        Environment.GetEnvironmentVariable("PADMON_DIAGNOSTIC") == "1";

    private readonly object _gate = new();
    private readonly Dictionary<uint, RuntimeDevice> _devices = [];

    private Thread? _thread;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool> _started = NewStartedSource();
    private int _livePreview;
    private bool _disposed;
    private string? _lastReconcileSignature;

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
            Name = "padmon-sdl"
        };
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
                    throw new TimeoutException("The SDL input thread did not stop in time.");
                }
            },
            cancellationToken).ConfigureAwait(false);

        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void SetLivePreview(bool enabled)
    {
        PadmonLog.Info($"live preview: {enabled}");
        Interlocked.Exchange(ref _livePreview, enabled ? 1 : 0);
    }

    public async ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(stableKey))
        {
            return false;
        }

        RuntimeDevice? runtime;

        lock (_gate)
        {
            runtime = _devices.Values.FirstOrDefault(
                candidate => string.Equals(candidate.Device.StableKey, stableKey, StringComparison.Ordinal));
        }

        if (runtime is null)
        {
            return false;
        }

        var low = (ushort)(Math.Clamp(lowFrequency, 0f, 1f) * ushort.MaxValue);
        var high = (ushort)(Math.Clamp(highFrequency, 0f, 1f) * ushort.MaxValue);
        var end = DateTimeOffset.UtcNow + duration;
        var anySucceeded = false;

        try
        {
            while (DateTimeOffset.UtcNow < end && !cancellationToken.IsCancellationRequested)
            {
                lock (_gate)
                {
                    // The device can disappear while the pulse is running. Never
                    // touch a handle after it has been removed from the live map.
                    if (!_devices.ContainsValue(runtime))
                    {
                        break;
                    }

                    var success = runtime.IsGamepad
                        ? SDL.RumbleGamepad(runtime.Handle, low, high, 100)
                        : SDL.RumbleJoystick(
                            runtime.Handle,
                            (short)(low / 2),
                            (short)(high / 2),
                            100);

                    anySucceeded |= success;
                }

                // Keep refreshing the motors. A single long SDL rumble packet
                // is dropped almost immediately by some XInput controllers.
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (_devices.ContainsValue(runtime))
                {
                    if (runtime.IsGamepad)
                    {
                        _ = SDL.RumbleGamepad(runtime.Handle, 0, 0, 1);
                    }
                    else
                    {
                        _ = SDL.RumbleJoystick(runtime.Handle, 0, 0, 1);
                    }
                }
            }
        }

        return anySucceeded;
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
            var flags = SDL.InitFlags.Gamepad
                | SDL.InitFlags.Joystick
                | SDL.InitFlags.Sensor
                | SDL.InitFlags.Events;

            // SDL's Windows joystick/gamepad backends otherwise stop
            // delivering input when the process has no focused window. Couch
            // Control must keep running while padmon is unfocused or hidden in
            // the tray, so opt in before SDL starts.
            SDL.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            PadmonLog.Info($"SDL init starting: {flags} (background events on)");

            if (!SDL.Init(flags))
            {
                var error = SDL.GetError();
                PadmonLog.Error($"SDL init failed: {error}");
                throw new InvalidOperationException($"SDL could not initialize: {error}");
            }

            PadmonLog.Info("SDL initialized");
            Reconcile();
            _started.TrySetResult(true);

            var nextReconcile = Environment.TickCount64 + ReconcileMilliseconds;
            var nextPublish = 0L;

            while (!cancellationToken.IsCancellationRequested)
            {
                var live = Volatile.Read(ref _livePreview) != 0;
                var timeout = live ? LivePollMilliseconds : IdlePollMilliseconds;

                if (SDL.WaitEventTimeout(out var sdlEvent, timeout))
                {
                    do
                    {
                        HandleEvent(sdlEvent);
                    }
                    while (SDL.PollEvent(out sdlEvent));
                }

                var now = Environment.TickCount64;

                if (now >= nextReconcile)
                {
                    Reconcile();
                    nextReconcile = now + ReconcileMilliseconds;
                }

                if (live && now >= nextPublish)
                {
                    PublishSnapshots();
                    nextPublish = now + LivePublishMilliseconds;
                }
            }
        }
        catch (Exception exception)
        {
            PadmonLog.Error("SDL thread failed", exception);
            _started.TrySetException(exception);
            ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            PadmonLog.Info("SDL thread stopping");
            CloseAll();
            SDL.Quit();
            _started.TrySetResult(false);
        }
    }

    private void Reconcile()
    {
        var gamepads = SDL.GetGamepads(out _) ?? [];
        var joysticks = SDL.GetJoysticks(out _) ?? [];

        var liveIds = new HashSet<uint>();
        foreach (var id in gamepads)
        {
            if (id != 0)
            {
                liveIds.Add(id);
            }
        }

        foreach (var id in joysticks)
        {
            if (id != 0)
            {
                liveIds.Add(id);
            }
        }

        var signature = string.Join(",", liveIds.OrderBy(id => id));
        if (Diagnostic || !string.Equals(signature, _lastReconcileSignature, StringComparison.Ordinal))
        {
            _lastReconcileSignature = signature;
            PadmonLog.Info(
                $"SDL reconcile: gamepads={gamepads.Length} joysticks={joysticks.Length} ids=[{signature}]");

            foreach (var id in liveIds)
            {
                var mapped = SDL.IsGamepad(id);
                var name = mapped ? SDL.GetGamepadNameForID(id) : SDL.GetJoystickNameForID(id);
                var vendor = mapped ? SDL.GetGamepadVendorForID(id) : SDL.GetJoystickVendorForID(id);
                var product = mapped ? SDL.GetGamepadProductForID(id) : SDL.GetJoystickProductForID(id);

                PadmonLog.Info(
                    $"SDL device id={id} mapped={mapped} name='{name ?? "?"}' vendor={vendor:X4} product={product:X4}");
            }
        }

        uint[] staleIds;
        lock (_gate)
        {
            staleIds = _devices.Keys.Where(id => !liveIds.Contains(id)).ToArray();
        }

        foreach (var id in staleIds)
        {
            RemoveDevice(id);
        }

        foreach (var id in liveIds)
        {
            AddDevice(id);
        }
    }

    private void AddDevice(uint instanceId)
    {
        lock (_gate)
        {
            if (_devices.ContainsKey(instanceId))
            {
                return;
            }
        }

        var isGamepad = SDL.IsGamepad(instanceId);
        PadmonLog.Info($"opening SDL device id={instanceId} mapped={isGamepad}");
        var handle = isGamepad ? SDL.OpenGamepad(instanceId) : SDL.OpenJoystick(instanceId);

        if (handle == IntPtr.Zero)
        {
            var error = SDL.GetError();
            PadmonLog.Error($"could not open SDL device id={instanceId}: {error}");
            ErrorOccurred?.Invoke(
                this,
                $"Could not open controller {instanceId}: {error}");
            return;
        }

        ControllerDevice device;
        try
        {
            device = Describe(instanceId, handle, isGamepad);
        }
        catch (Exception exception)
        {
            PadmonLog.Error($"could not describe SDL device id={instanceId}", exception);
            CloseHandle(handle, isGamepad);
            ErrorOccurred?.Invoke(this, $"Could not read controller {instanceId}: {exception.Message}");
            return;
        }

        var runtime = new RuntimeDevice(handle, isGamepad, device);
        bool added;

        lock (_gate)
        {
            added = _devices.TryAdd(instanceId, runtime);
        }

        if (!added)
        {
            CloseHandle(handle, isGamepad);
            return;
        }

        PadmonLog.Info(
            $"controller added: id={instanceId} name='{device.Name}' vendor={device.VendorId:X4} product={device.ProductId:X4} mapped={device.IsMapped} serial='{device.Serial}' path='{device.Path}' key='{device.StableKey}'");

        ConnectionChanged?.Invoke(this, new ControllerConnectionChangedEventArgs(device, true));
    }

    private void RemoveDevice(uint instanceId)
    {
        RuntimeDevice? runtime;
        lock (_gate)
        {
            if (!_devices.Remove(instanceId, out runtime))
            {
                return;
            }
        }

        PadmonLog.Info($"controller removed: id={instanceId} name='{runtime.Device.Name}' key='{runtime.Device.StableKey}'");
        CloseHandle(runtime.Handle, runtime.IsGamepad);
        ConnectionChanged?.Invoke(this, new ControllerConnectionChangedEventArgs(runtime.Device, false));
    }

    private void HandleEvent(SDL.Event sdlEvent)
    {
        var eventType = (SDL.EventType)sdlEvent.Type;

        if (eventType is SDL.EventType.GamepadAdded
            or SDL.EventType.JoystickAdded
            or SDL.EventType.GamepadRemoved
            or SDL.EventType.JoystickRemoved
            or SDL.EventType.JoystickBatteryUpdated
            or SDL.EventType.GamepadRemapped)
        {
            PadmonLog.Info($"SDL event: {eventType}");
        }

        switch (eventType)
        {
            case SDL.EventType.GamepadAdded:
                AddDevice(sdlEvent.GDevice.Which);
                break;

            case SDL.EventType.JoystickAdded:
                AddDevice(sdlEvent.JDevice.Which);
                break;

            case SDL.EventType.GamepadRemoved:
                RemoveDevice(sdlEvent.GDevice.Which);
                break;

            case SDL.EventType.JoystickRemoved:
                RemoveDevice(sdlEvent.JDevice.Which);
                break;

            case SDL.EventType.JoystickBatteryUpdated:
                UpdateBattery(sdlEvent.JBattery.Which, sdlEvent.JBattery.State, sdlEvent.JBattery.Percent);
                break;
        }
    }

    private void UpdateBattery(uint instanceId, SDL.PowerState state, int percent)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(instanceId, out var runtime))
            {
                return;
            }

            runtime.Device = runtime.Device with
            {
                BatteryPercent = percent is >= 0 and <= 100 ? percent : null,
                Power = MapPowerState(state)
            };
        }
    }

    private void PublishSnapshots()
    {
        List<ControllerSnapshot> snapshots = [];

        lock (_gate)
        {
            foreach (var runtime in _devices.Values)
            {
                try
                {
                    snapshots.Add(BuildSnapshot(runtime));
                }
                catch (Exception exception)
                {
                    ErrorOccurred?.Invoke(
                        this,
                        $"Could not read {runtime.Device.Name}: {exception.Message}");
                }
            }
        }

        if (snapshots.Count > 0)
        {
            SnapshotsAvailable?.Invoke(this, new ControllerSnapshotsEventArgs(snapshots));
        }
    }

    private static ControllerSnapshot BuildSnapshot(RuntimeDevice runtime)
    {
        var buttons = new List<ButtonState>();
        var axes = new List<AxisState>();
        var hats = new List<HatState>();

        if (runtime.IsGamepad)
        {
            for (var index = 0; index < (int)SDL.GamepadButton.Count; index++)
            {
                var button = (SDL.GamepadButton)index;
                if (!SDL.GamepadHasButton(runtime.Handle, button))
                {
                    continue;
                }

                var pressed = SDL.GetGamepadButton(runtime.Handle, button);
                buttons.Add(new ButtonState(
                    index,
                    LabelForGamepadButton(runtime.Handle, button),
                    pressed,
                    pressed ? 1f : 0f));
            }

            for (var index = 0; index < (int)SDL.GamepadAxis.Count; index++)
            {
                var axis = (SDL.GamepadAxis)index;
                if (!SDL.GamepadHasAxis(runtime.Handle, axis))
                {
                    continue;
                }

                var raw = SDL.GetGamepadAxis(runtime.Handle, axis);
                var trigger = axis is SDL.GamepadAxis.LeftTrigger or SDL.GamepadAxis.RightTrigger;
                var value = trigger
                    ? Math.Clamp(raw / (float)short.MaxValue, 0f, 1f)
                    : Math.Clamp(raw / (float)short.MaxValue, -1f, 1f);

                axes.Add(new AxisState(
                    index,
                    LabelForGamepadAxis(axis),
                    value,
                    raw,
                    trigger));
            }
        }
        else
        {
            var buttonCount = SDL.GetNumJoystickButtons(runtime.Handle);
            for (var index = 0; index < buttonCount; index++)
            {
                var pressed = SDL.GetJoystickButton(runtime.Handle, index);
                buttons.Add(new ButtonState(
                    index,
                    $"Button {index + 1}",
                    pressed,
                    pressed ? 1f : 0f));
            }

            var axisCount = SDL.GetNumJoystickAxes(runtime.Handle);
            for (var index = 0; index < axisCount; index++)
            {
                var raw = SDL.GetJoystickAxis(runtime.Handle, index);
                axes.Add(new AxisState(
                    index,
                    $"Axis {index + 1}",
                    Math.Clamp(raw / (float)short.MaxValue, -1f, 1f),
                    raw,
                    false));
            }

            var hatCount = SDL.GetNumJoystickHats(runtime.Handle);
            for (var index = 0; index < hatCount; index++)
            {
                hats.Add(new HatState(
                    index,
                    $"Hat {index + 1}",
                    (int)SDL.GetJoystickHat(runtime.Handle, index)));
            }
        }

        var percent = -1;
        var power = runtime.IsGamepad
            ? SDL.GetGamepadPowerInfo(runtime.Handle, out percent)
            : SDL.GetJoystickPowerInfo(runtime.Handle, out percent);

        var device = runtime.Device with
        {
            BatteryPercent = percent is >= 0 and <= 100 ? percent : null,
            Power = MapPowerState(power)
        };

        return new ControllerSnapshot(
            device,
            buttons,
            axes,
            hats,
            DateTimeOffset.UtcNow);
    }

    private static ControllerDevice Describe(uint instanceId, IntPtr handle, bool isGamepad)
    {
        var rawName = isGamepad
            ? SDL.GetGamepadName(handle)
            : SDL.GetJoystickName(handle);

        var name = string.IsNullOrWhiteSpace(rawName) ? "Controller" : rawName.Trim();

        var serial = TrimOrNull(
            isGamepad
                ? SDL.GetGamepadSerial(handle)
                : SDL.GetJoystickSerial(handle));

        var path = TrimOrNull(
            isGamepad
                ? SDL.GetGamepadPath(handle)
                : SDL.GetJoystickPath(handle));

        var vendor = isGamepad
            ? SDL.GetGamepadVendor(handle)
            : SDL.GetJoystickVendor(handle);

        var product = isGamepad
            ? SDL.GetGamepadProduct(handle)
            : SDL.GetJoystickProduct(handle);

        var guid = GetGuidString(instanceId, isGamepad);

        var connection = MapConnectionState(
            isGamepad
                ? SDL.GetGamepadConnectionState(handle)
                : SDL.GetJoystickConnectionState(handle));

        var percent = -1;
        var power = isGamepad
            ? SDL.GetGamepadPowerInfo(handle, out percent)
            : SDL.GetJoystickPowerInfo(handle, out percent);

        var kind = isGamepad
            ? MapGamepadType(SDL.GetGamepadType(handle))
            : MapJoystickType(SDL.GetJoystickType(handle));

        return new ControllerDevice(
            DeviceIdentity.Create(serial, guid, vendor, product, name),
            instanceId,
            name,
            serial,
            vendor,
            product,
            guid,
            kind,
            connection,
            percent is >= 0 and <= 100 ? percent : null,
            MapPowerState(power),
            isGamepad,
            path);
    }

    private static string? GetGuidString(uint instanceId, bool isGamepad)
    {
        var guid = isGamepad
            ? SDL.GetGamepadGUIDForID(instanceId)
            : SDL.GetJoystickGUIDForID(instanceId);

        var buffer = new byte[33];
        SDL.GUIDToString(guid, buffer, buffer.Length);

        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return length == 0 ? null : Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static string LabelForGamepadButton(IntPtr handle, SDL.GamepadButton button)
    {
        var label = SDL.GetGamepadButtonLabel(handle, button);
        var face = label switch
        {
            SDL.GamepadButtonLabel.A => "A",
            SDL.GamepadButtonLabel.B => "B",
            SDL.GamepadButtonLabel.X => "X",
            SDL.GamepadButtonLabel.Y => "Y",
            SDL.GamepadButtonLabel.Cross => "Cross",
            SDL.GamepadButtonLabel.Circle => "Circle",
            SDL.GamepadButtonLabel.Square => "Square",
            SDL.GamepadButtonLabel.Triangle => "Triangle",
            _ => null
        };

        if (face is not null)
        {
            return face;
        }

        return button switch
        {
            SDL.GamepadButton.Back => "Back",
            SDL.GamepadButton.Guide => "Guide",
            SDL.GamepadButton.Start => "Start",
            SDL.GamepadButton.LeftStick => "L3",
            SDL.GamepadButton.RightStick => "R3",
            SDL.GamepadButton.LeftShoulder => "LB",
            SDL.GamepadButton.RightShoulder => "RB",
            SDL.GamepadButton.DPadUp => "D-Pad Up",
            SDL.GamepadButton.DPadDown => "D-Pad Down",
            SDL.GamepadButton.DPadLeft => "D-Pad Left",
            SDL.GamepadButton.DPadRight => "D-Pad Right",
            SDL.GamepadButton.Misc1 => "Share",
            SDL.GamepadButton.RightPaddle1 => "Paddle R1",
            SDL.GamepadButton.LeftPaddle1 => "Paddle L1",
            SDL.GamepadButton.RightPaddle2 => "Paddle R2",
            SDL.GamepadButton.LeftPaddle2 => "Paddle L2",
            SDL.GamepadButton.Touchpad => "Touchpad",
            SDL.GamepadButton.Misc2 => "Misc 2",
            SDL.GamepadButton.Misc3 => "Misc 3",
            SDL.GamepadButton.Misc4 => "Misc 4",
            SDL.GamepadButton.Misc5 => "Misc 5",
            SDL.GamepadButton.Misc6 => "Misc 6",
            _ => button.ToString()
        };
    }

    private static string LabelForGamepadAxis(SDL.GamepadAxis axis) => axis switch
    {
        SDL.GamepadAxis.LeftX => "Left X",
        SDL.GamepadAxis.LeftY => "Left Y",
        SDL.GamepadAxis.RightX => "Right X",
        SDL.GamepadAxis.RightY => "Right Y",
        SDL.GamepadAxis.LeftTrigger => "LT",
        SDL.GamepadAxis.RightTrigger => "RT",
        _ => axis.ToString()
    };

    private static ControllerKind MapGamepadType(SDL.GamepadType type) => type switch
    {
        SDL.GamepadType.Unknown => ControllerKind.Gamepad,
        _ => ControllerKind.Gamepad
    };

    private static ControllerKind MapJoystickType(SDL.JoystickType type) => type switch
    {
        SDL.JoystickType.Gamepad => ControllerKind.Gamepad,
        SDL.JoystickType.Wheel => ControllerKind.Wheel,
        SDL.JoystickType.ArcadeStick => ControllerKind.ArcadeStick,
        SDL.JoystickType.FlightStick => ControllerKind.FlightStick,
        SDL.JoystickType.DancePad => ControllerKind.DancePad,
        SDL.JoystickType.Guitar => ControllerKind.Guitar,
        SDL.JoystickType.DrumKit => ControllerKind.DrumKit,
        SDL.JoystickType.ArcadePad => ControllerKind.ArcadePad,
        SDL.JoystickType.Throttle => ControllerKind.Throttle,
        _ => ControllerKind.Joystick
    };

    private static ConnectionKind MapConnectionState(SDL.JoystickConnectionState state) => state switch
    {
        SDL.JoystickConnectionState.Wired => ConnectionKind.Usb,
        SDL.JoystickConnectionState.Wireless => ConnectionKind.Bluetooth,
        _ => ConnectionKind.Unknown
    };

    private static PowerState MapPowerState(SDL.PowerState state) => state switch
    {
        SDL.PowerState.OnBattery => PowerState.OnBattery,
        SDL.PowerState.NoBattery => PowerState.Wired,
        SDL.PowerState.Charging => PowerState.Charging,
        SDL.PowerState.Charged => PowerState.Charged,
        _ => PowerState.Unknown
    };

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static TaskCompletionSource<bool> NewStartedSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void CloseAll()
    {
        RuntimeDevice[] runtimes;

        lock (_gate)
        {
            runtimes = _devices.Values.ToArray();
            _devices.Clear();
        }

        foreach (var runtime in runtimes)
        {
            CloseHandle(runtime.Handle, runtime.IsGamepad);
        }
    }

    private static void CloseHandle(IntPtr handle, bool isGamepad)
    {
        if (isGamepad)
        {
            SDL.CloseGamepad(handle);
        }
        else
        {
            SDL.CloseJoystick(handle);
        }
    }

    private sealed class RuntimeDevice(IntPtr handle, bool isGamepad, ControllerDevice device)
    {
        public IntPtr Handle { get; } = handle;

        public bool IsGamepad { get; } = isGamepad;

        public ControllerDevice Device { get; set; } = device;
    }
}

internal static class DeviceIdentity
{
    public static string Create(
        string? serial,
        string? sdlGuid,
        ushort vendorId,
        ushort productId,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            return $"serial:{vendorId:X4}:{productId:X4}:{serial.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(sdlGuid))
        {
            return $"guid:{sdlGuid.Trim().ToLowerInvariant()}";
        }

        return $"model:{vendorId:X4}:{productId:X4}:{name.Trim().ToLowerInvariant()}";
    }
}
