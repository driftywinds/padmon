using Padmon.Core.Devices;

namespace Padmon.Input;

public sealed class ControllerConnectionChangedEventArgs : EventArgs
{
    public ControllerConnectionChangedEventArgs(ControllerDevice device, bool connected)
    {
        Device = device;
        Connected = connected;
    }

    public ControllerDevice Device { get; }

    public bool Connected { get; }
}

public sealed class ControllerSnapshotsEventArgs : EventArgs
{
    public ControllerSnapshotsEventArgs(IReadOnlyList<ControllerSnapshot> snapshots)
    {
        Snapshots = snapshots;
    }

    public IReadOnlyList<ControllerSnapshot> Snapshots { get; }
}

public interface IControllerSource : IAsyncDisposable
{
    event EventHandler<ControllerConnectionChangedEventArgs>? ConnectionChanged;

    event EventHandler<ControllerSnapshotsEventArgs>? SnapshotsAvailable;

    event EventHandler<string>? ErrorOccurred;

    IReadOnlyList<ControllerDevice> ConnectedDevices { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables the 60 Hz live snapshot stream used by the controller tester.
    /// Background monitoring stays event-driven and cheap while this is off.
    /// </summary>
    void SetLivePreview(bool enabled);

    ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}
