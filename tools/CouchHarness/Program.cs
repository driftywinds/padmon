using Padmon.Core.Devices;
using Padmon.Core.Settings;
using Padmon.Couch;
using Padmon.Input;

var options = HarnessOptions.Parse(args);

if (options.Mode == HarnessMode.SelfTest)
{
    return SelfTests.Run();
}

if (options.Mode == HarnessMode.List)
{
    await ControllerProbe.ListAsync();
    return 0;
}

await ControllerProbe.WatchAsync(options);
return 0;

internal enum HarnessMode
{
    SelfTest,
    List,
    Watch
}

internal sealed record HarnessOptions(
    HarnessMode Mode,
    string? ControllerKey,
    bool Inject,
    TimeSpan Duration)
{
    public static HarnessOptions Parse(string[] args)
    {
        var mode = args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase))
            ? HarnessMode.SelfTest
            : args.Any(arg => string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase))
                ? HarnessMode.List
                : HarnessMode.Watch;

        string? controller = null;
        var inject = false;
        var duration = TimeSpan.FromSeconds(30);

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--controller" when index + 1 < args.Length:
                    controller = args[++index];
                    break;

                case "--inject":
                    inject = true;
                    break;

                case "--seconds" when index + 1 < args.Length &&
                                      int.TryParse(args[++index], out var seconds):
                    duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 600));
                    break;
            }
        }

        return new HarnessOptions(mode, controller, inject, duration);
    }
}

internal static class ControllerProbe
{
    public static async Task ListAsync()
    {
        await using var source = new SdlControllerSource();
        await source.StartAsync();
        source.SetLivePreview(true);

        try
        {
            var devices = source.ConnectedDevices;
            if (devices.Count == 0)
            {
                Console.WriteLine("No controllers detected.");
                return;
            }

            foreach (var device in devices)
            {
                Console.WriteLine(
                    $"{device.StableKey}\t{device.Name}\t{device.VendorId:X4}:{device.ProductId:X4}\t" +
                    $"mapped={device.IsMapped}\t{device.Connection}");
            }
        }
        finally
        {
            source.SetLivePreview(false);
            await source.StopAsync();
        }
    }

    public static async Task WatchAsync(HarnessOptions options)
    {
        var settings = new CouchControlSettings
        {
            Enabled = true,
            ControllerStableKey = options.ControllerKey
        };

        ICouchInputSink sink = options.Inject
            ? new SendInputSink()
            : new ConsoleSink();

        var engine = new CouchEngine(settings, sink);
        await using var source = new SdlControllerSource();
        var selectedKey = options.ControllerKey;

        source.SnapshotsAvailable += (_, args) =>
        {
            foreach (var snapshot in args.Snapshots)
            {
                if (selectedKey is null)
                {
                    selectedKey = snapshot.Device.StableKey;
                    Console.WriteLine($"Selected {snapshot.Device.Name} [{selectedKey}]");
                }

                if (!string.Equals(snapshot.Device.StableKey, selectedKey, StringComparison.Ordinal))
                {
                    continue;
                }

                engine.Process(snapshot, TimeSpan.FromMilliseconds(16));
            }
        };

        await source.StartAsync();
        source.SetLivePreview(true);

        if (options.ControllerKey is null)
        {
            var devices = source.ConnectedDevices;
            if (devices.Count > 0)
            {
                var device = devices[0];
                selectedKey = device.StableKey;
                Console.WriteLine($"Selected {device.Name} [{device.StableKey}]");
            }
        }

        Console.WriteLine(
            $"Couch harness watching for {options.Duration.TotalSeconds:0}s; input={(options.Inject ? "real" : "log")}. Ctrl+C to stop.");

        using var cancellation = new CancellationTokenSource(options.Duration);
        try
        {
            await Task.Delay(options.Duration, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            engine.ReleaseAll();
            source.SetLivePreview(false);
            await source.StopAsync();
            Console.WriteLine("Couch harness stopped.");
        }
    }
}

internal sealed class ConsoleSink : ICouchInputSink
{
    public void KeyDown(int virtualKey) => Console.WriteLine($"key down 0x{virtualKey:X2}");

    public void KeyUp(int virtualKey) => Console.WriteLine($"key up   0x{virtualKey:X2}");

    public void MouseButtonDown(int button) => Console.WriteLine($"mouse down {button}");

    public void MouseButtonUp(int button) => Console.WriteLine($"mouse up   {button}");

    public void MoveMouse(int dx, int dy) => Console.WriteLine($"mouse move {dx},{dy}");

    public void Scroll(int delta) => Console.WriteLine($"scroll {delta}");

    public void HideWindow() => Console.WriteLine("hide window");

    public void OpenOnScreenKeyboard() => Console.WriteLine("on-screen keyboard");
}

internal static class SelfTests
{
    private static int _failures;

    public static int Run()
    {
        Console.WriteLine("Couch Control self-tests");

        TestDefaultButtons();
        TestTriggers();
        TestRawDInputButton();
        TestPauseReleasesHeldKeys();
        TestPanicChord();
        TestAnalogMovement();
        TestHeldScrollRepeats();

        Console.WriteLine(_failures == 0 ? "PASS" : $"FAIL ({_failures})");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestDefaultButtons()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(new CouchControlSettings { Enabled = true }, sink);
        engine.Process(Snapshot((0, true)), TimeSpan.FromMilliseconds(16));
        engine.Process(Snapshot(), TimeSpan.FromMilliseconds(16));
        Check("A maps to left mouse", sink.Events.SequenceEqual(["mouse-down:0", "mouse-up:0"]), sink);
    }

    private static void TestTriggers()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(new CouchControlSettings { Enabled = true }, sink);
        engine.Process(Snapshot(axes: [(4, "LT", 1f, 32767, true)]), TimeSpan.FromMilliseconds(16));
        engine.Process(Snapshot(), TimeSpan.FromMilliseconds(16));
        Check("LT maps to space", sink.Events.SequenceEqual(["key-down:32", "key-up:32"]), sink);
    }

    private static void TestRawDInputButton()
    {
        var settings = new CouchControlSettings
        {
            Enabled = true,
            Bindings = new Dictionary<string, CouchBindingSettings>
            {
                [CouchControlIds.Button(2)] = new()
                {
                    Kind = CouchBindingKind.Keyboard,
                    Code = 0x58
                }
            }
        };

        var sink = new RecordingSink();
        var engine = new CouchEngine(settings, sink);
        engine.Process(Snapshot((2, true)), TimeSpan.FromMilliseconds(16));
        engine.Process(Snapshot(), TimeSpan.FromMilliseconds(16));
        Check("raw DInput button maps by index", sink.Events.SequenceEqual(["key-down:88", "key-up:88"]), sink);
    }

    private static void TestPauseReleasesHeldKeys()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(new CouchControlSettings { Enabled = true }, sink);
        engine.Process(Snapshot((1, true)), TimeSpan.FromMilliseconds(16));
        engine.Paused = true;
        Check("pause releases held key", sink.Events.SequenceEqual(["key-down:13", "key-up:13"]), sink);
    }

    private static void TestPanicChord()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(new CouchControlSettings { Enabled = true }, sink);
        engine.Process(Snapshot((7, true)), TimeSpan.FromMilliseconds(16));
        engine.Process(Snapshot((7, true), (8, true)), TimeSpan.FromMilliseconds(16));
        Check("L3+R3 disables couch control", !engine.Enabled, sink);

        engine.Process(Snapshot(), TimeSpan.FromMilliseconds(16));
        engine.Process(Snapshot((7, true), (8, true)), TimeSpan.FromMilliseconds(16));
        Check("L3+R3 re-enables couch control", engine.Enabled, sink);
    }

    private static void TestAnalogMovement()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(
            new CouchControlSettings { Enabled = true, CursorSpeed = 1000, DeadZone = 6000 },
            sink);
        engine.Process(
            Snapshot(axes: [(0, "Left X", 1f, 32767, false)]),
            TimeSpan.FromMilliseconds(100));

        Check("left stick emits mouse movement", sink.Events.Any(item => item.StartsWith("mouse-move:", StringComparison.Ordinal)), sink);
    }

    private static void TestHeldScrollRepeats()
    {
        var sink = new RecordingSink();
        var engine = new CouchEngine(
            new CouchControlSettings { Enabled = true, ScrollSpeed = 8, ScrollDeadZone = 5000 },
            sink);

        for (var index = 0; index < 60; index++)
        {
            engine.Process(
                Snapshot(axes: [(3, "Right Y", 1f, 32767, false)]),
                TimeSpan.FromMilliseconds(16));
        }

        var scrollCount = sink.Events.Count(item => item.StartsWith("scroll:", StringComparison.Ordinal));
        Check("held right stick repeats scrolling", scrollCount >= 4, sink);
    }

    private static ControllerSnapshot Snapshot(
        params (int Index, bool Pressed)[] buttons)
    {
        return CreateSnapshot(buttons, []);
    }

    private static ControllerSnapshot Snapshot(
        (int Index, string Label, float Value, short Raw, bool Trigger)[] axes,
        (int Index, bool Pressed)[]? buttons = null)
    {
        return CreateSnapshot(buttons ?? [], axes);
    }

    private static ControllerSnapshot CreateSnapshot(
        (int Index, bool Pressed)[] buttons,
        (int Index, string Label, float Value, short Raw, bool Trigger)[] axes)
    {
        var device = new ControllerDevice(
            "test:controller",
            1,
            "Test Controller",
            null,
            0x045E,
            0x02FD,
            null,
            ControllerKind.Gamepad,
            ConnectionKind.Usb,
            null,
            PowerState.Wired,
            true,
            null);

        return new ControllerSnapshot(
            device,
            buttons.Select(item => new ButtonState(
                item.Index,
                $"Button {item.Index}",
                item.Pressed,
                item.Pressed ? 1 : 0)).ToArray(),
            axes.Select(item => new AxisState(
                item.Index,
                item.Label,
                item.Value,
                item.Raw,
                item.Trigger)).ToArray(),
            [],
            DateTimeOffset.UtcNow);
    }

    private static void Check(string name, bool condition, RecordingSink sink)
    {
        if (condition)
        {
            Console.WriteLine($"  ok   {name}");
            return;
        }

        _failures++;
        Console.WriteLine($"  FAIL {name}");
        Console.WriteLine($"       events: {string.Join(", ", sink.Events)}");
    }
}

internal sealed class RecordingSink : ICouchInputSink
{
    public List<string> Events { get; } = [];

    public void KeyDown(int virtualKey) => Events.Add($"key-down:{virtualKey}");

    public void KeyUp(int virtualKey) => Events.Add($"key-up:{virtualKey}");

    public void MouseButtonDown(int button) => Events.Add($"mouse-down:{button}");

    public void MouseButtonUp(int button) => Events.Add($"mouse-up:{button}");

    public void MoveMouse(int dx, int dy) => Events.Add($"mouse-move:{dx},{dy}");

    public void Scroll(int delta) => Events.Add($"scroll:{delta}");

    public void HideWindow() => Events.Add("hide-window");

    public void OpenOnScreenKeyboard() => Events.Add("osk");
}
