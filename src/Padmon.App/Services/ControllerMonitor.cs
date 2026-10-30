using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;
using Padmon.Core.History;
using Padmon.Input;
using Padmon.ViewModels;

namespace Padmon.Services;

public sealed class ControllerMonitor : ObservableObject, IAsyncDisposable
{
    private readonly IControllerSource _source;
    private readonly IDeviceHistoryStore _history;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, ControllerViewModel> _byKey = new(StringComparer.Ordinal);

    private bool _startCompleted;
    private bool _disposed;
    private string? _error;

    public ControllerMonitor(
        IControllerSource source,
        IDeviceHistoryStore history,
        DispatcherQueue dispatcher)
    {
        _source = source;
        _history = history;
        _dispatcher = dispatcher;

        _source.ConnectionChanged += OnConnectionChanged;
        _source.SnapshotsAvailable += OnSnapshotsAvailable;
        _source.ErrorOccurred += OnSourceError;
    }

    public ObservableCollection<ControllerViewModel> Controllers { get; } = [];

    public event EventHandler<ControllerViewModel>? ControllerConnected;

    public event EventHandler<ControllerViewModel>? ControllerDisconnected;

    public event EventHandler<string>? ErrorOccurred;

    public bool IsMonitoring { get; private set; }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public int ConnectedCount => Controllers.Count;

    public string MonitoringLabel => IsMonitoring ? "Watching" : "Paused";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsMonitoring)
        {
            return;
        }

        PadmonLog.Info("controller monitor starting");
        await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        _startCompleted = true;
        PadmonLog.Info("controller monitor started");
        SetMonitoring(true);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMonitoring)
        {
            return;
        }

        foreach (var controller in Controllers.ToArray())
        {
            try
            {
                await _history.RecordDisconnectionAsync(
                    controller.StableKey,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await _source.StopAsync(cancellationToken).ConfigureAwait(false);
        SetMonitoring(false);
    }

    public async Task RenameAsync(
        string stableKey,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        await _history.RenameAsync(stableKey, displayName, cancellationToken).ConfigureAwait(false);

        _dispatcher.TryEnqueue(() =>
        {
            if (_byKey.TryGetValue(stableKey, out var controller))
            {
                controller.SetDisplayName(displayName ?? controller.HardwareName);
            }
        });
    }

    public void SetLivePreview(bool enabled)
    {
        _source.SetLivePreview(enabled);
    }

    public ValueTask<bool> RumbleAsync(
        string stableKey,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        return _source.RumbleAsync(stableKey, lowFrequency, highFrequency, duration, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.ConnectionChanged -= OnConnectionChanged;
        _source.SnapshotsAvailable -= OnSnapshotsAvailable;
        _source.ErrorOccurred -= OnSourceError;
        await StopAsync().ConfigureAwait(false);
        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private async void OnConnectionChanged(object? sender, ControllerConnectionChangedEventArgs args)
    {
        var initial = !_startCompleted;
        PadmonLog.Info(
            $"monitor connection event: connected={args.Connected} initial={initial} name='{args.Device.Name}' key='{args.Device.StableKey}'");

        try
        {
            if (args.Connected)
            {
                var result = await _history.RecordConnectionAsync(
                    args.Device,
                    DateTimeOffset.UtcNow).ConfigureAwait(false);

                PadmonLog.Info(
                    $"history connection recorded: key='{args.Device.StableKey}' isNew={result.IsNew} display='{result.Controller.DisplayName}'");

                _dispatcher.TryEnqueue(() => ApplyConnection(args, initial, result));
                return;
            }

            await _history.RecordDisconnectionAsync(
                args.Device.StableKey,
                DateTimeOffset.UtcNow).ConfigureAwait(false);

            PadmonLog.Info($"history disconnection recorded: key='{args.Device.StableKey}'");
            _dispatcher.TryEnqueue(() => ApplyDisconnection(args, initial));
        }
        catch (Exception exception)
        {
            PadmonLog.Error("controller monitor event failed", exception);
            _dispatcher.TryEnqueue(() =>
            {
                Error = exception.Message;
                ErrorOccurred?.Invoke(this, exception.Message);
            });
        }
    }

    private void ApplyConnection(
        ControllerConnectionChangedEventArgs args,
        bool initial,
        ConnectionRecordResult? record)
    {
        if (_byKey.TryGetValue(args.Device.StableKey, out var existing))
        {
            existing.ApplyDevice(args.Device);
            existing.IsNew = record?.IsNew ?? existing.IsNew;

            if (record is not null && !string.IsNullOrWhiteSpace(record.Controller.CustomName))
            {
                existing.SetDisplayName(record.Controller.CustomName);
            }

            return;
        }

        var controller = new ControllerViewModel(args.Device)
        {
            IsNew = record?.IsNew ?? false
        };

        if (record is not null && !string.IsNullOrWhiteSpace(record.Controller.CustomName))
        {
            controller.SetDisplayName(record.Controller.CustomName);
        }

        _byKey.Add(controller.StableKey, controller);
        Controllers.Add(controller);
        OnPropertyChanged(nameof(ConnectedCount));
        PadmonLog.Info($"UI controller added: name='{controller.DisplayName}' count={Controllers.Count}");

        if (!initial)
        {
            ControllerConnected?.Invoke(this, controller);
        }
    }

    private void ApplyDisconnection(ControllerConnectionChangedEventArgs args, bool initial)
    {
        if (!_byKey.Remove(args.Device.StableKey, out var removed))
        {
            return;
        }

        Controllers.Remove(removed);
        OnPropertyChanged(nameof(ConnectedCount));
        PadmonLog.Info($"UI controller removed: name='{removed.DisplayName}' count={Controllers.Count}");

        if (!initial)
        {
            ControllerDisconnected?.Invoke(this, removed);
        }
    }

    private void OnSnapshotsAvailable(object? sender, ControllerSnapshotsEventArgs args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            foreach (var snapshot in args.Snapshots)
            {
                if (_byKey.TryGetValue(snapshot.Device.StableKey, out var controller))
                {
                    controller.ApplySnapshot(snapshot);
                }
            }
        });
    }

    private void OnSourceError(object? sender, string message)
    {
        _dispatcher.TryEnqueue(() =>
        {
            Error = message;
            ErrorOccurred?.Invoke(this, message);
        });
    }

    private void SetMonitoring(bool value)
    {
        if (_dispatcher.HasThreadAccess)
        {
            IsMonitoring = value;
            OnPropertyChanged(nameof(IsMonitoring));
            OnPropertyChanged(nameof(MonitoringLabel));
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            IsMonitoring = value;
            OnPropertyChanged(nameof(IsMonitoring));
            OnPropertyChanged(nameof(MonitoringLabel));
        });
    }
}
