using System.Windows.Input;
using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class MacroEventBufferTests
{
    [Fact]
    public void AppendCapturedEvent_CoalescesConsecutiveMoveEvents()
    {
        MacroEventBuffer buffer = new();

        buffer.BeginRecording();
        buffer.AppendCapturedEvent(new(MacroEventType.Move, 10, 20, 0, TimeSpan.Zero), TimeSpan.FromMilliseconds(5));
        buffer.AppendCapturedEvent(new(MacroEventType.Move, 30, 40, 0, TimeSpan.Zero), TimeSpan.FromMilliseconds(20));

        MacroEvent recordedEvent = Assert.Single(buffer.Events);
        Assert.Equal(MacroEventType.Move, recordedEvent.Type);
        Assert.Equal(30, recordedEvent.X);
        Assert.Equal(40, recordedEvent.Y);
        Assert.Equal(TimeSpan.FromMilliseconds(20), recordedEvent.DelayBeforeEvent);
    }

    [Fact]
    public void ReplaceAll_ReplacesEventsAndResetsCount()
    {
        MacroEventBuffer buffer = new();

        buffer.ReplaceAll(
        [
            new(MacroEventType.LeftDown, 10, 20, 0, TimeSpan.FromMilliseconds(5)),
            new(MacroEventType.LeftUp, 10, 20, 0, TimeSpan.FromMilliseconds(10)),
        ]);

        Assert.Equal(2, buffer.Count);
        Assert.Equal("2 events", buffer.CountLabel);
    }

    [Fact]
    public void TrimTrailingShortcutEvents_RemovesOnlyTrailingShortcutKeys()
    {
        MacroEventBuffer buffer = new();
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Shift);

        buffer.ReplaceAll(
        [
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x41),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x11),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x10),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, binding.VirtualKey),
        ]);

        buffer.TrimTrailingShortcutEvents(binding);

        MacroEvent remainingEvent = Assert.Single(buffer.Events);
        Assert.Equal(0x41u, remainingEvent.VirtualKey);
    }
}
