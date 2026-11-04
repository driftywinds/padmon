using System.Runtime.InteropServices;
using Padmon.Core.Diagnostics;

namespace Padmon.Couch;

/// <summary>
/// Synthesizes keyboard and mouse input with SendInput. This is intentionally
/// a clean-room implementation; no Gopher360 source is used.
/// </summary>
public sealed class SendInputSink : ICouchInputSink
{
    private static readonly bool Trace =
        string.Equals(
            Environment.GetEnvironmentVariable("PADMON_COUCH_TRACE"),
            "1",
            StringComparison.Ordinal);

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;

    private const uint KeyEventUp = 0x0002;

    public void KeyDown(int virtualKey) => SendKeyboard(virtualKey, up: false);

    public void KeyUp(int virtualKey) => SendKeyboard(virtualKey, up: true);

    public void MouseButtonDown(int button)
    {
        var flag = button switch
        {
            0 => MouseLeftDown,
            1 => MouseRightDown,
            2 => MouseMiddleDown,
            _ => 0u
        };

        if (flag != 0)
        {
            SendMouse(flag, 0, 0, 0);
        }
    }

    public void MouseButtonUp(int button)
    {
        var flag = button switch
        {
            0 => MouseLeftUp,
            1 => MouseRightUp,
            2 => MouseMiddleUp,
            _ => 0u
        };

        if (flag != 0)
        {
            SendMouse(flag, 0, 0, 0);
        }
    }

    public void MoveMouse(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        // Gopher360 moves the cursor with SetCursorPos rather than a relative
        // SendInput move. Absolute positioning is also more predictable when
        // the foreground window changes, which is exactly what happens when a
        // Couch Control click activates another app.
        if (!GetCursorPos(out var point))
        {
            if (Trace)
            {
                PadmonLog.Error($"couch GetCursorPos failed: {Marshal.GetLastWin32Error()}");
            }

            return;
        }

        if (!SetCursorPos(point.X + dx, point.Y + dy) && Trace)
        {
            PadmonLog.Error($"couch SetCursorPos failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Scroll(int delta)
    {
        if (delta != 0)
        {
            SendMouse(MouseWheel, 0, 0, delta * 120);
        }
    }

    public void HideWindow()
    {
        // The app wires this to its own window/tray logic.
    }

    public void OpenOnScreenKeyboard()
    {
        // Phase 2 wires this to the Windows on-screen keyboard.
    }

    private static void SendKeyboard(int virtualKey, bool up)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)virtualKey,
                    Flags = up ? KeyEventUp : 0
                }
            }
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<Input>());
        TraceResult("keyboard", sent);
    }

    private static void SendMouse(uint flags, int dx, int dy, int data)
    {
        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = unchecked((uint)data),
                    Flags = flags
                }
            }
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<Input>());
        TraceResult("mouse", sent);
    }

    private static void TraceResult(string kind, uint sent)
    {
        if (sent == 0 && Trace)
        {
            PadmonLog.Error($"couch SendInput {kind} failed: {Marshal.GetLastWin32Error()}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
