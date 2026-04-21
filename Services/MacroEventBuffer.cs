using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class MacroEventBuffer
{
    private readonly List<MacroEvent> _events = [];
    private TimeSpan _lastRecordedOffset = TimeSpan.Zero;

    public IReadOnlyList<MacroEvent> Events => _events;

    public int Count => _events.Count;

    public bool HasEvents => _events.Count > 0;

    public string CountLabel => Count == 1 ? "1 event" : $"{Count} events";

    public List<MacroEvent> Snapshot()
    {
        return [.. _events];
    }

    public void BeginRecording()
    {
        _events.Clear();
        _lastRecordedOffset = TimeSpan.Zero;
    }

    public void ReplaceAll(IEnumerable<MacroEvent> events)
    {
        _events.Clear();
        _events.AddRange(events);
        _lastRecordedOffset = TimeSpan.Zero;
    }

    public void AppendCapturedEvent(MacroEvent capturedEvent, TimeSpan currentOffset, bool coalesceConsecutiveMoves = true)
    {
        TimeSpan delay = currentOffset - _lastRecordedOffset;
        _lastRecordedOffset = currentOffset;

        MacroEvent timedEvent = capturedEvent with { DelayBeforeEvent = delay };

        if (coalesceConsecutiveMoves
            && _events.Count > 0
            && _events[^1].Type == MacroEventType.Move
            && timedEvent.Type == MacroEventType.Move)
        {
            MacroEvent previousMoveEvent = _events[^1];
            _events[^1] = timedEvent with
            {
                DelayBeforeEvent = previousMoveEvent.DelayBeforeEvent + timedEvent.DelayBeforeEvent,
            };
            return;
        }

        _events.Add(timedEvent);
    }

    public void TrimTrailingShortcutEvents(HotkeyBinding binding)
    {
        HashSet<uint> shortcutVirtualKeys = HotkeyService.GetShortcutVirtualKeys(binding);

        while (_events.Count > 0)
        {
            MacroEvent lastEvent = _events[^1];
            if (lastEvent.Type is not MacroEventType.KeyDown and not MacroEventType.KeyUp)
            {
                break;
            }

            if (!shortcutVirtualKeys.Contains(lastEvent.VirtualKey))
            {
                break;
            }

            _events.RemoveAt(_events.Count - 1);
        }
    }
}
