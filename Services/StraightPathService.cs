using Sreapeat.Models;

namespace Sreapeat.Services;

internal static class StraightPathService
{
    public static IReadOnlyList<MacroEvent> TransformForPlayback(IReadOnlyList<MacroEvent> events)
    {
        if (events.Count == 0)
        {
            return events;
        }

        List<MacroEvent> transformedEvents = new(events.Count);
        (int X, int Y)? lastMouseAnchor = null;

        for (int index = 0; index < events.Count;)
        {
            MacroEvent currentEvent = events[index];
            if (currentEvent.Type != MacroEventType.Move)
            {
                transformedEvents.Add(currentEvent);

                if (HasMousePosition(currentEvent))
                {
                    lastMouseAnchor = (currentEvent.X, currentEvent.Y);
                }

                index++;
                continue;
            }

            int runStartIndex = index;
            while (index < events.Count && events[index].Type == MacroEventType.Move)
            {
                index++;
            }

            AddStraightPathRun(
                events,
                runStartIndex,
                index,
                lastMouseAnchor,
                index < events.Count && HasMousePosition(events[index])
                    ? (events[index].X, events[index].Y)
                    : null,
                transformedEvents,
                out lastMouseAnchor);
        }

        return transformedEvents;
    }

    private static void AddStraightPathRun(
        IReadOnlyList<MacroEvent> events,
        int runStartIndex,
        int runEndIndexExclusive,
        (int X, int Y)? startAnchor,
        (int X, int Y)? nextAnchor,
        ICollection<MacroEvent> transformedEvents,
        out (int X, int Y)? endAnchor)
    {
        int moveCount = runEndIndexExclusive - runStartIndex;
        MacroEvent firstMove = events[runStartIndex];
        MacroEvent lastMove = events[runEndIndexExclusive - 1];

        (int X, int Y) startPoint = startAnchor ?? (firstMove.X, firstMove.Y);
        (int X, int Y) endPoint = nextAnchor ?? (lastMove.X, lastMove.Y);

        long totalTicks = 0;
        for (int index = runStartIndex; index < runEndIndexExclusive; index++)
        {
            totalTicks += events[index].DelayBeforeEvent.Ticks;
        }

        long elapsedTicks = 0;
        for (int offset = 0; offset < moveCount; offset++)
        {
            MacroEvent originalMove = events[runStartIndex + offset];
            elapsedTicks += originalMove.DelayBeforeEvent.Ticks;

            double progress = totalTicks > 0
                ? Math.Clamp(elapsedTicks / (double)totalTicks, 0.0, 1.0)
                : (offset + 1d) / moveCount;

            transformedEvents.Add(originalMove with
            {
                X = Lerp(startPoint.X, endPoint.X, progress),
                Y = Lerp(startPoint.Y, endPoint.Y, progress),
            });
        }

        endAnchor = endPoint;
    }

    private static bool HasMousePosition(MacroEvent macroEvent)
    {
        return macroEvent.Type is MacroEventType.Move
            or MacroEventType.LeftDown
            or MacroEventType.LeftUp
            or MacroEventType.RightDown
            or MacroEventType.RightUp
            or MacroEventType.MiddleDown
            or MacroEventType.MiddleUp
            or MacroEventType.Wheel;
    }

    private static int Lerp(int start, int end, double progress)
    {
        return (int)Math.Round(start + ((end - start) * progress), MidpointRounding.AwayFromZero);
    }
}
