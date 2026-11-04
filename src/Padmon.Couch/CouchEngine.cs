using Padmon.Core.Devices;
using Padmon.Core.Settings;

namespace Padmon.Couch;

/// <summary>
/// Deterministic couch mapping engine. It consumes controller snapshots from
/// padmon's SDL pipeline and emits keyboard/mouse actions. It has no UI or
/// platform-window dependencies, so the CLI harness can exercise it without
/// hardware.
/// </summary>
public sealed class CouchEngine
{
    private const int LeftStickButton = 7;
    private const int RightStickButton = 8;

    private readonly CouchControlSettings _settings;
    private readonly ICouchInputSink _sink;
    private readonly Dictionary<int, int> _keyCounts = [];
    private readonly Dictionary<int, int> _mouseCounts = [];
    private readonly HashSet<string> _previous = new(StringComparer.Ordinal);
    private Dictionary<string, CouchBindingSettings> _bindings;

    private double _moveX;
    private double _moveY;
    private double _scroll;
    private bool _chordLatched;
    private bool _paused;

    public CouchEngine(CouchControlSettings settings, ICouchInputSink sink)
    {
        _settings = settings;
        _sink = sink;
        _bindings = CouchDefaults.Merge(settings.Bindings);
        Enabled = settings.Enabled;
    }

    public bool Enabled { get; private set; }

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            if (value)
            {
                ReleaseAll();
            }
        }
    }

    public event EventHandler? EnabledChanged;

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        if (!enabled)
        {
            ReleaseAll();
        }

        EnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadBindings()
    {
        ReleaseAll();
        _bindings = CouchDefaults.Merge(_settings.Bindings);
    }

    public void Process(ControllerSnapshot snapshot, TimeSpan elapsed)
    {
        var pressed = BuildPressed(snapshot);
        var chord = pressed.Contains(CouchControlIds.Button(LeftStickButton)) &&
                    pressed.Contains(CouchControlIds.Button(RightStickButton));

        if (chord && !_chordLatched)
        {
            _chordLatched = true;
            SetEnabled(!Enabled);
        }
        else if (!chord)
        {
            _chordLatched = false;
        }

        if (!Enabled || Paused)
        {
            ReleaseAll();
            return;
        }

        if (chord)
        {
            // Never let the panic chord fire L3/R3's normal actions.
            pressed.Remove(CouchControlIds.Button(LeftStickButton));
            pressed.Remove(CouchControlIds.Button(RightStickButton));
        }

        foreach (var id in _previous.Union(pressed))
        {
            var down = pressed.Contains(id);
            var wasDown = _previous.Contains(id);

            if (down == wasDown || !_bindings.TryGetValue(id, out var binding))
            {
                continue;
            }

            ApplyBinding(binding, down);
        }

        ApplyMouse(snapshot, elapsed);

        _previous.Clear();
        _previous.UnionWith(pressed);
    }

    public void ReleaseAll()
    {
        foreach (var pair in _keyCounts)
        {
            if (pair.Value > 0)
            {
                _sink.KeyUp(pair.Key);
            }
        }

        foreach (var pair in _mouseCounts)
        {
            if (pair.Value > 0)
            {
                _sink.MouseButtonUp(pair.Key);
            }
        }

        _keyCounts.Clear();
        _mouseCounts.Clear();
        _previous.Clear();
        _moveX = 0;
        _moveY = 0;
        _scroll = 0;
    }

    private void ApplyBinding(CouchBindingSettings binding, bool down)
    {
        switch (binding.Kind)
        {
            case CouchBindingKind.Keyboard:
                ApplyKey(binding.Code, down);
                break;

            case CouchBindingKind.MouseButton:
                ApplyMouseButton(binding.Code, down);
                break;

            case CouchBindingKind.HideWindow when down:
                _sink.HideWindow();
                break;

            case CouchBindingKind.OnScreenKeyboard when down:
                _sink.OpenOnScreenKeyboard();
                break;
        }
    }

    private void ApplyKey(int virtualKey, bool down)
    {
        _keyCounts.TryGetValue(virtualKey, out var count);

        if (down)
        {
            if (count == 0)
            {
                _sink.KeyDown(virtualKey);
            }

            _keyCounts[virtualKey] = count + 1;
            return;
        }

        if (count <= 1)
        {
            _keyCounts.Remove(virtualKey);
            if (count == 1)
            {
                _sink.KeyUp(virtualKey);
            }

            return;
        }

        _keyCounts[virtualKey] = count - 1;
    }

    private void ApplyMouseButton(int button, bool down)
    {
        _mouseCounts.TryGetValue(button, out var count);

        if (down)
        {
            if (count == 0)
            {
                _sink.MouseButtonDown(button);
            }

            _mouseCounts[button] = count + 1;
            return;
        }

        if (count <= 1)
        {
            _mouseCounts.Remove(button);
            if (count == 1)
            {
                _sink.MouseButtonUp(button);
            }

            return;
        }

        _mouseCounts[button] = count - 1;
    }

    private void ApplyMouse(ControllerSnapshot snapshot, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        var seconds = Math.Min(elapsed.TotalSeconds, 0.1);
        var leftX = GetAxis(snapshot, "Left X", 0);
        var leftY = GetAxis(snapshot, "Left Y", 1);
        var rightX = GetAxis(snapshot, "Right X", 2);
        var rightY = GetAxis(snapshot, "Right Y", 3);

        if (_settings.SwapSticks)
        {
            (leftX, rightX) = (rightX, leftX);
            (leftY, rightY) = (rightY, leftY);
        }

        var deadZone = Math.Clamp(_settings.DeadZone / 32767.0, 0, 0.99);
        var scrollDeadZone = Math.Clamp(_settings.ScrollDeadZone / 32767.0, 0, 0.99);
        var moveX = Shape(leftX, deadZone);
        var moveY = Shape(leftY, deadZone);
        var scrollY = Shape(rightY, scrollDeadZone);

        var acceleration = 1 + Math.Max(0, _settings.Acceleration) * Math.Abs(moveX);
        _moveX += moveX * _settings.CursorSpeed * acceleration * seconds;
        _moveY += moveY * _settings.CursorSpeed * acceleration * seconds;

        var dx = (int)Math.Truncate(_moveX);
        var dy = (int)Math.Truncate(_moveY);
        if (dx != 0 || dy != 0)
        {
            _sink.MoveMouse(dx, dy);
            _moveX -= dx;
            _moveY -= dy;
        }

        _scroll += -scrollY * _settings.ScrollSpeed * seconds;
        var scrollNotches = (int)Math.Truncate(_scroll);
        if (scrollNotches != 0)
        {
            _sink.Scroll(scrollNotches);
            _scroll -= scrollNotches;
        }
    }

    private static double Shape(double value, double deadZone)
    {
        var magnitude = Math.Abs(value);
        if (magnitude <= deadZone)
        {
            return 0;
        }

        var normalized = (magnitude - deadZone) / (1 - deadZone);
        return Math.Sign(value) * normalized;
    }

    private static float GetAxis(ControllerSnapshot snapshot, string label, int fallbackIndex)
    {
        var match = snapshot.Axes.FirstOrDefault(
            axis => string.Equals(axis.Label, label, StringComparison.OrdinalIgnoreCase));

        if (match.Label is not null)
        {
            return match.Value;
        }

        var byIndex = snapshot.Axes.FirstOrDefault(axis => axis.Index == fallbackIndex);
        return byIndex.Label is null ? 0 : byIndex.Value;
    }

    private static HashSet<string> BuildPressed(ControllerSnapshot snapshot)
    {
        var pressed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var button in snapshot.Buttons)
        {
            if (button.Pressed)
            {
                pressed.Add(CouchControlIds.Button(button.Index));
            }
        }

        foreach (var axis in snapshot.Axes)
        {
            if (!axis.IsTrigger || axis.Value < 0.5f)
            {
                continue;
            }

            pressed.Add(
                string.Equals(axis.Label, "RT", StringComparison.OrdinalIgnoreCase) || axis.Index == 5
                    ? CouchControlIds.TriggerRight
                    : CouchControlIds.TriggerLeft);
        }

        foreach (var hat in snapshot.Hats)
        {
            if ((hat.Value & 1) != 0)
            {
                pressed.Add(CouchControlIds.Hat(hat.Index, CouchControlIds.HatUp));
            }

            if ((hat.Value & 2) != 0)
            {
                pressed.Add(CouchControlIds.Hat(hat.Index, CouchControlIds.HatRight));
            }

            if ((hat.Value & 4) != 0)
            {
                pressed.Add(CouchControlIds.Hat(hat.Index, CouchControlIds.HatDown));
            }

            if ((hat.Value & 8) != 0)
            {
                pressed.Add(CouchControlIds.Hat(hat.Index, CouchControlIds.HatLeft));
            }
        }

        return pressed;
    }
}
