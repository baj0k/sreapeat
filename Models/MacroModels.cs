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
    uint VirtualKey = 0,
    ushort ScanCode = 0,
    bool IsExtendedKey = false,
    bool IsInjected = false);

internal sealed record HotkeyBinding(string DisplayText, uint Modifiers, uint VirtualKey);
