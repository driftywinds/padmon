using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;
using Padmon.Input;

namespace Padmon.Services;

/// <summary>
/// Merges SDL with the Windows.Gaming.Input fallback. SDL remains the primary
/// source; WGI is used for devices SDL does not enumerate. A WGI device is
/// suppressed when SDL already covers the same vendor/product pair.
/// </summary>
public sealed class CompositeControllerSource : IControllerSource, IAsyncDisposable
{
    private readonly IControllerSource _primary;
    private readonly IControllerSource _secondary;
    private readonly object _gate = new();
    private readonly Dictionary<string, ControllerDevice> _primaryDevices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControllerDevice> _secondaryDevices = new(StringComparer.Ordinal);

    private bool _disposed;

    public CompositeControllerSource(IControllerSource primary, IControllerSource secondary)
    {
        _primary = primary;
        _secondary = secondary;

        _primary.ConnectionChanged += OnPrimaryConnectionChanged;
        _primary.SnapshotsAvailable += OnPrimarySnapshots;
        _primary.ErrorOccurred += OnPrimaryError;

        _secondary.ConnectionChanged += OnSecondaryConnectionChanged;
        _secondary.SnapshotsAvailable += OnSecondarySnapshots;
        _secondary.ErrorOccurred += OnSecondaryError;
    }

    public event EventHandler<ControllerConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler<ControllerSnapshotsEventArgs>? SnapshotsAvailable;

    public event EventHandler<string>? ErrorOccurred;

    public IReadOnlyList<ControllerDevice> ConnectedDevices
    {
        get
        {
            lock (_gate)
            {
                return _primaryDevices.Values
                    .Concat(_secondaryDevices.Values)
                    .ToArray();
            }
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _primary.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _secondary.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            PadmonLog.Error("WGI fallback source failed to start; continuing with SDL only", exception);
            ErrorOccurred?.Invoke(this, $"Windows controller fallback unavailable: {exception.Message}");
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _secondary.StopAsync(cancellationToken).ConfigureAwait(false);
        await _primary.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetLivePreview(bool enabled)
    {
        _primary.SetLivePreview(enabled);
        _secondary.SetLivePreview(enabled);
    }

    public async ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        bool primaryOwns;

        lock (_gate)
        {
            primaryOwns = _primaryDevices.ContainsKey(stableKey);
        }

        if (primaryOwns)
        {
            return await _primary.RumbleAsync(
                stableKey,
                lowFrequency,
                highFrequency,
                duration,
                cancellationToken).ConfigureAwait(false);
        }

        return await _secondary.RumbleAsync(
            stableKey,
            lowFrequency,
            highFrequency,
            duration,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _primary.ConnectionChanged -= OnPrimaryConnectionChanged;
        _primary.SnapshotsAvailable -= OnPrimarySnapshots;
        _primary.ErrorOccurred -= OnPrimaryError;

        _secondary.ConnectionChanged -= OnSecondaryConnectionChanged;
        _secondary.SnapshotsAvailable -= OnSecondarySnapshots;
        _secondary.ErrorOccurred -= OnSecondaryError;

        await _secondary.DisposeAsync().ConfigureAwait(false);
        await _primary.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPrimaryConnectionChanged(object? sender, ControllerConnectionChangedEventArgs args)
    {
        if (args.Connected)
        {
            ControllerDevice? superseded = null;

            lock (_gate)
            {
                _primaryDevices[args.Device.StableKey] = args.Device;

                var duplicateKey = _secondaryDevices
                    .Where(pair => SameModel(pair.Value, args.Device))
                    .Select(pair => pair.Key)
                    .FirstOrDefault();

                if (duplicateKey is not null)
                {
                    _secondaryDevices.Remove(duplicateKey, out superseded);
                }
            }

            if (superseded is not null)
            {
                PadmonLog.Info(
                    $"WGI fallback superseded by SDL: name='{superseded.Name}' vendor={superseded.VendorId:X4} product={superseded.ProductId:X4}");
                ConnectionChanged?.Invoke(
                    this,
                    new ControllerConnectionChangedEventArgs(superseded, false));
            }

            ConnectionChanged?.Invoke(this, args);
            return;
        }

        lock (_gate)
        {
            _primaryDevices.Remove(args.Device.StableKey);
        }

        ConnectionChanged?.Invoke(this, args);
    }

    private void OnSecondaryConnectionChanged(object? sender, ControllerConnectionChangedEventArgs args)
    {
        if (args.Connected)
        {
            bool covered;

            lock (_gate)
            {
                covered = _primaryDevices.Values.Any(primary => SameModel(primary, args.Device));

                if (!covered)
                {
                    _secondaryDevices[args.Device.StableKey] = args.Device;
                }
            }

            if (!covered)
            {
                PadmonLog.Info($"WGI fallback accepted: name='{args.Device.Name}' key='{args.Device.StableKey}'");
                ConnectionChanged?.Invoke(this, args);
            }
            else
            {
                PadmonLog.Info(
                    $"WGI fallback ignored (already covered by SDL): name='{args.Device.Name}' vendor={args.Device.VendorId:X4} product={args.Device.ProductId:X4}");
            }

            return;
        }

        bool removed;
        lock (_gate)
        {
            removed = _secondaryDevices.Remove(args.Device.StableKey);
        }

        if (removed)
        {
            ConnectionChanged?.Invoke(this, args);
        }
    }

    private void OnPrimarySnapshots(object? sender, ControllerSnapshotsEventArgs args)
    {
        SnapshotsAvailable?.Invoke(this, args);
    }

    private void OnSecondarySnapshots(object? sender, ControllerSnapshotsEventArgs args)
    {
        bool hasSecondary;
        lock (_gate)
        {
            hasSecondary = _secondaryDevices.Count > 0;
        }

        if (hasSecondary)
        {
            SnapshotsAvailable?.Invoke(this, args);
        }
    }

    private void OnPrimaryError(object? sender, string message) => ErrorOccurred?.Invoke(this, message);

    private void OnSecondaryError(object? sender, string message) => ErrorOccurred?.Invoke(this, message);

    private static bool SameModel(ControllerDevice left, ControllerDevice right)
    {
        return left.VendorId == right.VendorId
            && left.ProductId == right.ProductId;
    }
}
