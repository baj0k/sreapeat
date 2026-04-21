using System.ComponentModel;
using System.Runtime.InteropServices;
using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal static class PlaybackTiming
{
    public static TimeSpan ScaleDelay(TimeSpan originalDelay, double speedMultiplier)
    {
        double scaledTicks = originalDelay.Ticks / speedMultiplier;
        if (scaledTicks <= 1)
        {
            return TimeSpan.Zero;
        }

        if (scaledTicks > long.MaxValue)
        {
            return TimeSpan.FromTicks(long.MaxValue);
        }

        return TimeSpan.FromTicks((long)scaledTicks);
    }
}

internal sealed class PlaybackService : IPlaybackService
{
    public async Task PlayAsync(
        IReadOnlyList<MacroEvent> events,
        int repeatCount,
        bool loopForever,
        double speedMultiplier,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        int iterationsRemaining = repeatCount;

        while (!cancellationToken.IsCancellationRequested && (loopForever || iterationsRemaining > 0))
        {
            foreach (MacroEvent macroEvent in events)
            {
                if (macroEvent.DelayBeforeEvent > TimeSpan.Zero)
                {
                    TimeSpan scaledDelay = PlaybackTiming.ScaleDelay(macroEvent.DelayBeforeEvent, speedMultiplier);
                    if (scaledDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(scaledDelay, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                Execute(macroEvent);
            }

            if (!loopForever)
            {
                iterationsRemaining--;
            }
        }
    }

    private static void Execute(MacroEvent macroEvent)
    {
        if (IsMouseEvent(macroEvent.Type))
        {
            if (!NativeMethods.SetCursorPos(macroEvent.X, macroEvent.Y))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to move the mouse cursor during playback.");
            }
        }

        if (macroEvent.Type is MacroEventType.KeyDown or MacroEventType.KeyUp)
        {
            bool useScanCode = macroEvent.ScanCode != 0;
            uint keyFlags = 0;

            if (useScanCode)
            {
                keyFlags |= NativeMethods.KeyEventFScancode;
            }

            if (macroEvent.IsExtendedKey)
            {
                keyFlags |= NativeMethods.KeyEventFExtendedKey;
            }

            if (macroEvent.Type == MacroEventType.KeyUp)
            {
                keyFlags |= NativeMethods.KeyEventFKeyUp;
            }

            NativeMethods.Input keyboardInput = new()
            {
                Type = NativeMethods.InputKeyboard,
                Union = new NativeMethods.InputUnion
                {
                    KeyboardInput = new NativeMethods.KeyboardInput
                    {
                        WVk = useScanCode ? (ushort)0 : (ushort)macroEvent.VirtualKey,
                        WScan = macroEvent.ScanCode,
                        DwFlags = keyFlags,
                    },
                },
            };

            SendSingleInput(keyboardInput, "Unable to send a keyboard event during playback.");
            return;
        }

        uint? flag = macroEvent.Type switch
        {
            MacroEventType.Move => null,
            MacroEventType.LeftDown => NativeMethods.MouseEventFLeftDown,
            MacroEventType.LeftUp => NativeMethods.MouseEventFLeftUp,
            MacroEventType.RightDown => NativeMethods.MouseEventFRightDown,
            MacroEventType.RightUp => NativeMethods.MouseEventFRightUp,
            MacroEventType.MiddleDown => NativeMethods.MouseEventFMiddleDown,
            MacroEventType.MiddleUp => NativeMethods.MouseEventFMiddleUp,
            MacroEventType.Wheel => NativeMethods.MouseEventFWheel,
            _ => null,
        };

        if (flag is null)
        {
            return;
        }

        NativeMethods.Input input = new()
        {
            Type = NativeMethods.InputMouse,
            Union = new NativeMethods.InputUnion
            {
                MouseInput = new NativeMethods.MouseInput
                {
                    MouseData = macroEvent.Type == MacroEventType.Wheel
                        ? unchecked((uint)macroEvent.Delta)
                        : 0,
                    DwFlags = flag.Value,
                },
            },
        };

        SendSingleInput(input, "Unable to send a mouse event during playback.");
    }

    private static void SendSingleInput(NativeMethods.Input input, string errorMessage)
    {
        uint sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.Input>());
        if (sent == 1)
        {
            return;
        }

        throw new Win32Exception(Marshal.GetLastWin32Error(), errorMessage);
    }

    private static bool IsMouseEvent(MacroEventType eventType)
    {
        return eventType is MacroEventType.Move
            or MacroEventType.LeftDown
            or MacroEventType.LeftUp
            or MacroEventType.RightDown
            or MacroEventType.RightUp
            or MacroEventType.MiddleDown
            or MacroEventType.MiddleUp
            or MacroEventType.Wheel;
    }
}
