using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Padmon.Core.Devices;
using Padmon.Core.Settings;
using Padmon.Couch;
using Padmon.Input;

namespace Padmon.Services;

/// <summary>
/// Owns the Couch Control runtime. It is deliberately gated on unpackaged
/// builds, because the Store package is not allowed to ship input injection.
/// </summary>
public sealed class CouchControlService : IDisposable
{
    private readonly ControllerMonitor _monitor;
    private readonly Func<PadmonSettings> _settings;
    private readonly ISettingsStore _store;
    private readonly DispatcherQueue _dispatcher;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CouchAppSink _sink;

    private CouchEngine? _engine;
    private TimeSpan _lastSnapshot;
    private bool _enabled;
    private bool _syncingEnabled;
    private bool _disposed;
    private string _status = "Off";

    public CouchControlService(
        ControllerMonitor monitor,
        Func<PadmonSettings> settings,
        ISettingsStore store,
        DispatcherQueue dispatcher)
    {
        _monitor = monitor;
        _settings = settings;
        _store = store;
        _dispatcher = dispatcher;
        _sink = new CouchAppSink(this);
        _monitor.SnapshotsReceived += OnSnapshotsReceived;
    }

    public static bool IsAvailable => !AppEnvironment.IsPackaged;

    public bool Enabled => _enabled;

    public string Status => _status;

    public event EventHandler? StateChanged;

    public void SyncFromSettings()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsAvailable)
        {
            _enabled = false;
            StopEngine();
            SetStatus("Unavailable in the Microsoft Store build.");
            return;
        }

        var control = _settings().CouchControl;
        _enabled = control.Enabled;

        if (_enabled)
        {
            EnsureControllerSelected(control);
            StartEngine();
        }
        else
        {
            StopEngine();
            _monitor.SetCouchPreview(false);
        }

        SetStatus(_enabled ? "Starting…" : "Off");
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || !IsAvailable || _enabled == enabled)
        {
            return;
        }

        var control = _settings().CouchControl;
        _enabled = enabled;
        control.Enabled = enabled;

        _syncingEnabled = true;
        try
        {
            if (enabled)
            {
                EnsureControllerSelected(control);
                if (_engine is { } engine)
                {
                    engine.SetEnabled(true);
                }
                else
                {
                    StartEngine();
                }

                _monitor.SetCouchPreview(true);
            }
            else
            {
                StopEngine();
                _monitor.SetCouchPreview(false);
            }
        }
        finally
        {
            _syncingEnabled = false;
        }

        Save();
        SetStatus(enabled ? "Active" : "Off");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetController(string? stableKey)
    {
        if (_disposed)
        {
            return;
        }

        var control = _settings().CouchControl;
        control.ControllerStableKey = string.IsNullOrWhiteSpace(stableKey) ? null : stableKey;
        _lastSnapshot = TimeSpan.Zero;
        _engine?.ReleaseAll();
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.SnapshotsReceived -= OnSnapshotsReceived;
        StopEngine();
        _monitor.SetCouchPreview(false);
    }

    private void EnsureControllerSelected(CouchControlSettings control)
    {
        if (!string.IsNullOrWhiteSpace(control.ControllerStableKey))
        {
            return;
        }

        var first = _monitor.Controllers.FirstOrDefault();
        if (first is not null)
        {
            control.ControllerStableKey = first.StableKey;
        }
    }

    private void StartEngine()
    {
        if (_engine is not null || _disposed)
        {
            return;
        }

        var control = _settings().CouchControl;
        _engine = new CouchEngine(control, _sink);
        _engine.EnabledChanged += OnEngineEnabledChanged;
        _engine.SetEnabled(true);
        _monitor.SetCouchPreview(true);
        _lastSnapshot = TimeSpan.Zero;
    }

    private void StopEngine()
    {
        if (_engine is null)
        {
            return;
        }

        var engine = _engine;
        _engine = null;
        engine.EnabledChanged -= OnEngineEnabledChanged;
        engine.SetEnabled(false);
        engine.ReleaseAll();
        _lastSnapshot = TimeSpan.Zero;
    }

    private void OnEngineEnabledChanged(object? sender, EventArgs e)
    {
        if (_syncingEnabled || sender is not CouchEngine engine)
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || engine != _engine)
            {
                return;
            }

            _enabled = engine.Enabled;
            _settings().CouchControl.Enabled = _enabled;
            _monitor.SetCouchPreview(_enabled);
            Save();
            SetStatus(_enabled ? "Active" : "Off");
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSnapshotsReceived(object? sender, ControllerSnapshotsEventArgs args)
    {
        var engine = _engine;
        if (!_enabled || engine is null)
        {
            return;
        }

        var control = _settings().CouchControl;
        if (string.IsNullOrWhiteSpace(control.ControllerStableKey))
        {
            return;
        }

        ControllerSnapshot? selected = null;
        foreach (var snapshot in args.Snapshots)
        {
            if (string.Equals(
                    snapshot.Device.StableKey,
                    control.ControllerStableKey,
                    StringComparison.Ordinal))
            {
                selected = snapshot;
                break;
            }
        }

        if (selected is null)
        {
            engine.ReleaseAll();
            SetStatus("Waiting for the selected controller.");
            return;
        }

        var now = _clock.Elapsed;
        var elapsed = _lastSnapshot == TimeSpan.Zero ? TimeSpan.FromMilliseconds(16) : now - _lastSnapshot;
        _lastSnapshot = now;
        if (elapsed > TimeSpan.FromMilliseconds(100))
        {
            elapsed = TimeSpan.FromMilliseconds(16);
        }

        engine.Paused = control.PauseWhenPadmonFocused && IsPadmonForeground();
        engine.Process(selected, elapsed);

        SetStatus(engine.Paused
            ? "Paused while padmon is focused."
            : $"Active · {selected.Device.Name}");
    }

    private static bool IsPadmonForeground()
    {
        if (App.MainWindow is not MainWindow mainWindow)
        {
            return false;
        }

        var mainHandle = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        return mainHandle != IntPtr.Zero && GetForegroundWindow() == mainHandle;
    }

    private void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            _ = _store.SaveAsync(_settings());
        }
        catch
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private sealed class CouchAppSink(CouchControlService owner) : ICouchInputSink
    {
        private readonly SendInputSink _input = new();

        public void KeyDown(int virtualKey) => _input.KeyDown(virtualKey);

        public void KeyUp(int virtualKey) => _input.KeyUp(virtualKey);

        public void MouseButtonDown(int button) => _input.MouseButtonDown(button);

        public void MouseButtonUp(int button) => _input.MouseButtonUp(button);

        public void MoveMouse(int dx, int dy) => _input.MoveMouse(dx, dy);

        public void Scroll(int delta) => _input.Scroll(delta);

        public void HideWindow()
        {
            owner._dispatcher.TryEnqueue(() =>
            {
                if (App.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.HideToTray();
                }
            });
        }

        public void OpenOnScreenKeyboard()
        {
            owner._dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "osk.exe");

                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch
                {
                }
            });
        }
    }
}
