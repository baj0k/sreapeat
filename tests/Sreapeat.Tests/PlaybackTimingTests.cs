using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class PlaybackTimingTests
{
    [Fact]
    public void ScaleDelay_DividesDelayBySpeedMultiplier()
    {
        TimeSpan scaledDelay = PlaybackTiming.ScaleDelay(TimeSpan.FromMilliseconds(200), 2.0);

        Assert.Equal(TimeSpan.FromMilliseconds(100), scaledDelay);
    }

    [Fact]
    public void ScaleDelay_WhenRoundedBelowOneTick_ReturnsZero()
    {
        TimeSpan scaledDelay = PlaybackTiming.ScaleDelay(TimeSpan.FromTicks(1), 2.0);

        Assert.Equal(TimeSpan.Zero, scaledDelay);
    }

    [Fact]
    public void ScaleDelay_WhenScaledTicksExceedLongMaxValue_ReturnsMaxTimeSpan()
    {
        TimeSpan hugeDelay = TimeSpan.FromTicks(long.MaxValue / 2);
        TimeSpan result = PlaybackTiming.ScaleDelay(hugeDelay, 0.1);

        Assert.Equal(TimeSpan.FromTicks(long.MaxValue), result);
    }

    [Fact]
    public void ContainsKeyboardEvents_ReturnsTrueWhenAnyKeyboardEventExists()
    {
        MacroEvent[] events =
        [
            new(MacroEventType.Move, 10, 20, 0, TimeSpan.Zero),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.FromMilliseconds(10), 0x52),
        ];

        bool containsKeyboardEvents = MacroEventInspector.ContainsKeyboardEvents(events);

        Assert.True(containsKeyboardEvents);
    }
}
