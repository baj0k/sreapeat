using System.Runtime.InteropServices;
using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class PlaybackService
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
                    TimeSpan scaledDelay = ScaleDelay(macroEvent.DelayBeforeEvent, speedMultiplier);
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

    private static TimeSpan ScaleDelay(TimeSpan originalDelay, double speedMultiplier)
    {
        double scaledTicks = originalDelay.Ticks / speedMultiplier;
        if (scaledTicks <= 1)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)scaledTicks);
    }

    private static void Execute(MacroEvent macroEvent)
    {
        NativeMethods.SetCursorPos(macroEvent.X, macroEvent.Y);

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

        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.Input>());
    }
}
