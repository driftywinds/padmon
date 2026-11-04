using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Padmon.Core.Devices;
using Padmon.Core.Diagnostics;
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
    private bool _padmonActive = true;
    private bool _lastPaused;
    private bool _lastLoggedEnabled;
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

    public CouchControlSettings Settings => _settings().CouchControl;

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

        PadmonLog.Info($"couch set enabled: {enabled}");
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

    public void SetPadmonActive(bool active)
    {
        _padmonActive = active;
    }

    public void SaveSettings()
    {
        if (!_disposed)
        {
            Save();
        }
    }

    public void ReloadBindings()
    {
        _engine?.ReloadBindings();
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
        PadmonLog.Info("couch engine stopped");
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
            PadmonLog.Info($"couch chord set enabled: {_enabled}");
            // Keep the snapshot stream alive while the engine object exists.
            // The L3+R3 chord turns the engine off, and the same chord must be
            // able to turn it back on without reopening the page.
            _monitor.SetCouchPreview(true);
            Save();
            SetStatus(_enabled ? "Active" : "Off");
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSnapshotsReceived(object? sender, ControllerSnapshotsEventArgs args)
    {
        var engine = _engine;
        if (engine is null)
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
            if (_enabled)
            {
                SetStatus("Waiting for the selected controller.");
            }

            return;
        }

        var now = _clock.Elapsed;
        var elapsed = _lastSnapshot == TimeSpan.Zero ? TimeSpan.FromMilliseconds(16) : now - _lastSnapshot;
        _lastSnapshot = now;
        if (elapsed > TimeSpan.FromMilliseconds(100))
        {
            elapsed = TimeSpan.FromMilliseconds(16);
        }

        // Keep feeding the engine while it is off so the same L3+R3 chord can
        // turn it back on. The engine itself suppresses normal bindings then.
        var foreground = GetForegroundWindow();
        var mainHandle = GetMainWindowHandle();
        var isPadmon = _padmonActive || (mainHandle != IntPtr.Zero && foreground == mainHandle);
        var paused = control.PauseWhenPadmonFocused && isPadmon;

        if (paused != _lastPaused || _enabled != _lastLoggedEnabled)
        {
            _lastPaused = paused;
            _lastLoggedEnabled = _enabled;
            PadmonLog.Info(
                $"couch focus: enabled={_enabled} engine={engine.Enabled} paused={paused} active={_padmonActive} foreground=0x{foreground.ToInt64():X} main=0x{mainHandle.ToInt64():X}");
        }

        engine.Paused = paused;
        engine.Process(selected, elapsed);

        if (!_enabled)
        {
            return;
        }

        SetStatus(engine.Paused
            ? "Paused while padmon is focused — switch to another app."
            : $"Active · {selected.Device.Name}");
    }

    private static IntPtr GetMainWindowHandle()
    {
        return App.MainWindow is MainWindow mainWindow
            ? WinRT.Interop.WindowNative.GetWindowHandle(mainWindow)
            : IntPtr.Zero;
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
        private static readonly bool Trace =
            string.Equals(
                Environment.GetEnvironmentVariable("PADMON_COUCH_TRACE"),
                "1",
                StringComparison.Ordinal);

        private readonly SendInputSink _input = new();
        private long _moveTraceCounter;

        public void KeyDown(int virtualKey)
        {
            TraceAction($"key down 0x{virtualKey:X2}");
            _input.KeyDown(virtualKey);
        }

        public void KeyUp(int virtualKey)
        {
            TraceAction($"key up 0x{virtualKey:X2}");
            _input.KeyUp(virtualKey);
        }

        public void MouseButtonDown(int button)
        {
            TraceAction($"mouse down {button}");
            _input.MouseButtonDown(button);
        }

        public void MouseButtonUp(int button)
        {
            TraceAction($"mouse up {button}");
            _input.MouseButtonUp(button);
        }

        public void MoveMouse(int dx, int dy)
        {
            if (Trace && (++_moveTraceCounter % 30 == 0))
            {
                PadmonLog.Info($"couch trace: move {dx},{dy}");
            }

            _input.MoveMouse(dx, dy);
        }

        public void Scroll(int delta)
        {
            TraceAction($"scroll {delta}");
            _input.Scroll(delta);
        }

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

        private static void TraceAction(string message)
        {
            if (Trace)
            {
                PadmonLog.Info($"couch trace: {message}");
            }
        }
    }
}
