using System.Runtime.InteropServices;
using Padmon.Core.Diagnostics;

namespace Padmon.Services;

internal static class XInputRumble
{
    public static async Task<bool> PulseAsync(
        float lowFrequency,
        float highFrequency,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var indices = FindConnectedIndices();
        if (indices.Count == 0)
        {
            PadmonLog.Info("XInput rumble: no connected slot");
            return false;
        }

        var low = (ushort)(Math.Clamp(lowFrequency, 0f, 1f) * ushort.MaxValue);
        var high = (ushort)(Math.Clamp(highFrequency, 0f, 1f) * ushort.MaxValue);
        var vibration = new XInputVibration { LeftMotorSpeed = low, RightMotorSpeed = high };
        var anySucceeded = false;

        foreach (var index in indices)
        {
            var result = XInputSetState(index, ref vibration);
            if (result == 0)
            {
                anySucceeded = true;
                PadmonLog.Info($"XInput rumble: slot={index} low={low} high={high}");
            }
            else
            {
                PadmonLog.Info($"XInput rumble failed: slot={index} result={result}");
            }
        }

        if (!anySucceeded)
        {
            return false;
        }

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            var stop = default(XInputVibration);
            foreach (var index in indices)
            {
                _ = XInputSetState(index, ref stop);
            }
        }

        return true;
    }

    private static List<uint> FindConnectedIndices()
    {
        var indices = new List<uint>();

        for (uint index = 0; index < 4; index++)
        {
            if (XInputGetState(index, out _) == 0)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(uint index, ref XInputVibration vibration);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }
}
