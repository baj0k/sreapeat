namespace Sreapeat.Models;

internal enum MacroEventType
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel,
    KeyDown,
    KeyUp,
}

internal sealed record MacroEvent(
    MacroEventType Type,
    int X,
    int Y,
    int Delta,
    TimeSpan DelayBeforeEvent,
    uint VirtualKey = 0);
