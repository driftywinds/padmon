using System.Runtime.InteropServices;
using Padmon.Core.Diagnostics;

namespace Padmon.Couch;

/// <summary>
/// Synthesizes keyboard and mouse input with SendInput. This is intentionally
/// a clean-room implementation; no Gopher360 source is used.
/// </summary>
public sealed class SendInputSink : ICouchInputSink
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;

    private const uint MouseMove = 0x0001;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
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

        if (!GetCursorPos(out var point))
        {
            return;
        }

        var targetX = point.X + dx;
        var targetY = point.Y + dy;

        // Gopher360 moves the cursor with SetCursorPos. Some desktops report
        // success without actually moving, so verify and fall back to absolute
        // SendInput if the position did not change.
        SetCursorPos(targetX, targetY);
        if (GetCursorPos(out var actual) &&
            Math.Abs(actual.X - targetX) <= 1 &&
            Math.Abs(actual.Y - targetY) <= 1)
        {
            return;
        }

        PadmonLog.Error(
            $"couch SetCursorPos missed target {targetX},{targetY}; at {actual.X},{actual.Y}");
        SendAbsoluteMove(targetX, targetY);
    }

    private static void SendAbsoluteMove(int targetX, int targetY)
    {
        var left = GetSystemMetrics(SystemMetricXVirtualScreen);
        var top = GetSystemMetrics(SystemMetricYVirtualScreen);
        var width = GetSystemMetrics(SystemMetricVirtualScreenWidth);
        var height = GetSystemMetrics(SystemMetricVirtualScreenHeight);
        if (width <= 1 || height <= 1)
        {
            return;
        }

        targetX = Math.Clamp(targetX, left, left + width - 1);
        targetY = Math.Clamp(targetY, top, top + height - 1);
        var normalizedX = (int)Math.Round((targetX - left) * 65535.0 / (width - 1));
        var normalizedY = (int)Math.Round((targetY - top) * 65535.0 / (height - 1));

        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = normalizedX,
                    Dy = normalizedY,
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk
                }
            }
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<Input>());
        TraceResult("absolute move", sent);
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
        // mouse_event is the older injection path. It uses the same system
        // input queue as SendInput but some shells are more reliable with it
        // when the calling process is not the foreground process.
        mouse_event(flags, unchecked((uint)dx), unchecked((uint)dy), unchecked((uint)data), UIntPtr.Zero);
    }

    private static void TraceResult(string kind, uint sent)
    {
        if (sent == 0)
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
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
