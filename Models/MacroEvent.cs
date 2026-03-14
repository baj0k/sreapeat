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
}

internal sealed record MacroEvent(
    MacroEventType Type,
    int X,
    int Y,
    int Delta,
    TimeSpan DelayBeforeEvent);
