#if DEBUG
using Padmon.Core.Devices;
using Padmon.Input;

namespace Padmon.Services;

/// <summary>
/// Development-only source used to exercise the shell and tester without
/// physical hardware. Enable with PADMON_SIMULATE=1.
/// </summary>
internal sealed class SimulatedControllerSource : IControllerSource
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ControllerDevice _device;
    private Task? _loop;
    private int _livePreview;

    public SimulatedControllerSource()
    {
        _device = new ControllerDevice(
            "simulated:gamesir-cyclone-2",
            9001,
            "GameSir Cyclone 2",
            "SIM-0001",
            0x3537,
            0x1001,
            "03000000353701100000000000000000",
            ControllerKind.Gamepad,
            ConnectionKind.Usb,
            82,
            PowerState.OnBattery,
            true,
            @"\\?\HID#VID_3537&PID_1001#simulated");
    }

    public event EventHandler<ControllerConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler<ControllerSnapshotsEventArgs>? SnapshotsAvailable;

#pragma warning disable CS0067
    public event EventHandler<string>? ErrorOccurred;
#pragma warning restore CS0067

    public IReadOnlyList<ControllerDevice> ConnectedDevices => [_device];

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ConnectionChanged?.Invoke(this, new ControllerConnectionChangedEventArgs(_device, true));
        _loop = RunAsync(_cancellation.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _cancellation.Cancel();

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        ConnectionChanged?.Invoke(this, new ControllerConnectionChangedEventArgs(_device, false));
    }

    public void SetLivePreview(bool enabled)
    {
        Interlocked.Exchange(ref _livePreview, enabled ? 1 : 0);
    }

    public ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(string.Equals(stableKey, _device.StableKey, StringComparison.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var flap = Environment.GetEnvironmentVariable("PADMON_SIMULATE_FLAP") == "1";
        var nextFlap = started.AddSeconds(6);
        var disconnected = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(16, cancellationToken).ConfigureAwait(false);

            if (flap && DateTimeOffset.UtcNow >= nextFlap)
            {
                disconnected = !disconnected;
                ConnectionChanged?.Invoke(
                    this,
                    new ControllerConnectionChangedEventArgs(_device, !disconnected));
                nextFlap = DateTimeOffset.UtcNow.AddSeconds(disconnected ? 3 : 9);
            }

            if (disconnected || Volatile.Read(ref _livePreview) == 0)
            {
                continue;
            }

            var seconds = (float)(DateTimeOffset.UtcNow - started).TotalSeconds;
            var leftX = MathF.Sin(seconds * 1.3f) * 0.9f;
            var leftY = MathF.Cos(seconds * 1.1f) * 0.55f;
            var rightX = MathF.Sin(seconds * 0.7f) * 0.35f;
            var rightY = MathF.Cos(seconds * 0.9f) * 0.25f;
            var leftTrigger = (MathF.Sin(seconds * 0.8f) + 1f) / 2f;
            var rightTrigger = (MathF.Sin(seconds * 0.55f + 1.4f) + 1f) / 2f;
            var south = MathF.Sin(seconds * 2.4f) > 0.75f;
            var east = MathF.Sin(seconds * 1.7f) > 0.9f;
            var leftShoulder = MathF.Sin(seconds * 1.1f) > 0.82f;
            var rightShoulder = MathF.Sin(seconds * 0.9f + 0.8f) > 0.85f;

            var buttons = new List<ButtonState>
            {
                new(0, "A", south, south ? 1f : 0f),
                new(1, "B", east, east ? 1f : 0f),
                new(2, "X", false, 0f),
                new(3, "Y", false, 0f),
                new(4, "Back", false, 0f),
                new(5, "Guide", false, 0f),
                new(6, "Start", false, 0f),
                new(7, "L3", false, 0f),
                new(8, "R3", false, 0f),
                new(9, "LB", leftShoulder, leftShoulder ? 1f : 0f),
                new(10, "RB", rightShoulder, rightShoulder ? 1f : 0f),
                new(11, "D-Pad Up", false, 0f),
                new(12, "D-Pad Down", false, 0f),
                new(13, "D-Pad Left", false, 0f),
                new(14, "D-Pad Right", false, 0f)
            };

            var axes = new List<AxisState>
            {
                new(0, "Left X", leftX, (short)(leftX * short.MaxValue), false),
                new(1, "Left Y", leftY, (short)(leftY * short.MaxValue), false),
                new(2, "Right X", rightX, (short)(rightX * short.MaxValue), false),
                new(3, "Right Y", rightY, (short)(rightY * short.MaxValue), false),
                new(4, "LT", leftTrigger, (short)(leftTrigger * short.MaxValue), true),
                new(5, "RT", rightTrigger, (short)(rightTrigger * short.MaxValue), true)
            };

            var device = _device with
            {
                BatteryPercent = 82 - (int)(seconds / 30),
                Power = PowerState.OnBattery
            };

            SnapshotsAvailable?.Invoke(
                this,
                new ControllerSnapshotsEventArgs(
                [
                    new ControllerSnapshot(
                        device,
                        buttons,
                        axes,
                        [],
                        DateTimeOffset.UtcNow)
                ]));
        }
    }
}
#endif
