using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class StraightPathServiceTests
{
    [Fact]
    public void TransformForPlayback_UsesMouseTargetsAsStraightPathAnchors()
    {
        MacroEvent[] events =
        [
            new(MacroEventType.LeftUp, 0, 0, 0, TimeSpan.Zero),
            new(MacroEventType.Move, 20, 80, 0, TimeSpan.FromMilliseconds(10)),
            new(MacroEventType.Move, 60, 20, 0, TimeSpan.FromMilliseconds(15)),
            new(MacroEventType.LeftDown, 100, 100, 0, TimeSpan.FromMilliseconds(5)),
        ];

        IReadOnlyList<MacroEvent> transformed = StraightPathService.TransformForPlayback(events);

        Assert.Equal(4, transformed.Count);
        Assert.Equal(new MacroEvent(MacroEventType.Move, 40, 40, 0, TimeSpan.FromMilliseconds(10)), transformed[1]);
        Assert.Equal(new MacroEvent(MacroEventType.Move, 100, 100, 0, TimeSpan.FromMilliseconds(15)), transformed[2]);
        Assert.Equal(events[3], transformed[3]);
    }

    [Fact]
    public void TransformForPlayback_DoesNotMoveAcrossKeyboardEvents()
    {
        MacroEvent[] events =
        [
            new(MacroEventType.LeftUp, 10, 10, 0, TimeSpan.Zero),
            new(MacroEventType.Move, 50, 70, 0, TimeSpan.FromMilliseconds(10)),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.FromMilliseconds(5), 0x41),
            new(MacroEventType.Move, 80, 20, 0, TimeSpan.FromMilliseconds(10)),
            new(MacroEventType.LeftDown, 110, 10, 0, TimeSpan.FromMilliseconds(5)),
        ];

        IReadOnlyList<MacroEvent> transformed = StraightPathService.TransformForPlayback(events);

        Assert.Equal(events[1], transformed[1]);
        Assert.Equal(events[2], transformed[2]);
        Assert.Equal(new MacroEvent(MacroEventType.Move, 110, 10, 0, TimeSpan.FromMilliseconds(10)), transformed[3]);
    }

    [Fact]
    public void TransformForPlayback_WhenMoveDelaysAreZero_SpreadsProgressEvenly()
    {
        MacroEvent[] events =
        [
            new(MacroEventType.LeftUp, 0, 0, 0, TimeSpan.Zero),
            new(MacroEventType.Move, 5, 5, 0, TimeSpan.Zero),
            new(MacroEventType.Move, 10, 10, 0, TimeSpan.Zero),
            new(MacroEventType.Move, 15, 15, 0, TimeSpan.Zero),
            new(MacroEventType.LeftDown, 90, 0, 0, TimeSpan.Zero),
        ];

        IReadOnlyList<MacroEvent> transformed = StraightPathService.TransformForPlayback(events);

        Assert.Equal(30, transformed[1].X);
        Assert.Equal(60, transformed[2].X);
        Assert.Equal(90, transformed[3].X);
        Assert.Equal(0, transformed[1].Y);
        Assert.Equal(0, transformed[2].Y);
        Assert.Equal(0, transformed[3].Y);
    }
}
