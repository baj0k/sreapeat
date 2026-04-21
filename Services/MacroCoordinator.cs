using System.Diagnostics;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal interface IHookService
{
    bool IsRunning { get; }

    void Start();

    void Stop();
}

internal interface IPlaybackService
{
    Task PlayAsync(
        IReadOnlyList<MacroEvent> events,
        int repeatCount,
        bool loopForever,
        double speedMultiplier,
        CancellationToken cancellationToken);
}

internal static class MacroEventInspector
{
    public static bool ContainsKeyboardEvents(IEnumerable<MacroEvent> events)
    {
        return events.Any(static macroEvent => macroEvent.Type is MacroEventType.KeyDown or MacroEventType.KeyUp);
    }
}

internal enum KeyboardCaptureOutcome
{
    None,
    EventRecorded,
    StopRecording,
    StopPlayback,
}

internal enum PlaybackRunStatus
{
    Completed,
    Cancelled,
    Failed,
}

internal sealed record PlaybackLaunchResult(bool Started, Task<PlaybackRunResult>? PlaybackTask = null, Exception? Failure = null);

internal sealed record PlaybackRunResult(PlaybackRunStatus Status, Exception? Failure = null);

internal sealed class MacroCoordinator
{
    private readonly IHookService _keyboardHookService;
    private readonly MacroEventBuffer _macroEventBuffer;
    private readonly IHookService _mouseHookService;
    private readonly IPlaybackService _playbackService;
    private readonly Action _resumeHotkeys;
    private readonly MacroRuntimeSession _runtimeSession;
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly Action _suspendHotkeys;

    public MacroCoordinator(
        MacroEventBuffer macroEventBuffer,
        MacroRuntimeSession runtimeSession,
        IHookService mouseHookService,
        IHookService keyboardHookService,
        IPlaybackService playbackService,
        Action suspendHotkeys,
        Action resumeHotkeys)
    {
        _macroEventBuffer = macroEventBuffer;
        _runtimeSession = runtimeSession;
        _mouseHookService = mouseHookService;
        _keyboardHookService = keyboardHookService;
        _playbackService = playbackService;
        _suspendHotkeys = suspendHotkeys;
        _resumeHotkeys = resumeHotkeys;
    }

    public bool StartRecording(bool recordKeyboardActions, bool recordAllMouseMoves = false)
    {
        if (!_runtimeSession.TryBeginRecording(recordKeyboardActions, recordAllMouseMoves))
        {
            return false;
        }

        List<MacroEvent> previousEvents = _macroEventBuffer.Snapshot();
        bool suspendedHotkeys = false;

        _macroEventBuffer.BeginRecording();
        _recordingStopwatch.Restart();

        try
        {
            if (recordKeyboardActions)
            {
                _suspendHotkeys();
                suspendedHotkeys = true;
                _runtimeSession.ClearPressedPhysicalKeys();
            }

            _mouseHookService.Start();

            if (recordKeyboardActions)
            {
                _keyboardHookService.Start();
            }
        }
        catch
        {
            if (_keyboardHookService.IsRunning)
            {
                _keyboardHookService.Stop();
            }

            if (_mouseHookService.IsRunning)
            {
                _mouseHookService.Stop();
            }

            _macroEventBuffer.ReplaceAll(previousEvents);
            _runtimeSession.StopRecording(out _);

            if (suspendedHotkeys)
            {
                _resumeHotkeys();
            }

            throw;
        }

        return true;
    }

    public bool StopRecording(HotkeyBinding recordHotkey, bool trimTrailingShortcutEvents = false)
    {
        if (!_runtimeSession.StopRecording(out bool restoreHotkeys))
        {
            return false;
        }

        try
        {
            if (_keyboardHookService.IsRunning)
            {
                _keyboardHookService.Stop();
            }

            if (_mouseHookService.IsRunning)
            {
                _mouseHookService.Stop();
            }
        }
        finally
        {
            _recordingStopwatch.Stop();
        }

        if (trimTrailingShortcutEvents)
        {
            _macroEventBuffer.TrimTrailingShortcutEvents(recordHotkey);
        }

        if (restoreHotkeys)
        {
            _resumeHotkeys();
        }

        return true;
    }

    public bool TryRecordMouseEvent(MacroEvent capturedEvent)
    {
        if (!_runtimeSession.IsRecording)
        {
            return false;
        }

        _macroEventBuffer.AppendCapturedEvent(
            capturedEvent,
            _recordingStopwatch.Elapsed,
            coalesceConsecutiveMoves: !_runtimeSession.RecordAllMouseMoves);
        return true;
    }

    public KeyboardCaptureOutcome HandleKeyboardCaptured(
        MacroEvent capturedEvent,
        HotkeyBinding recordHotkey,
        HotkeyBinding playHotkey)
    {
        if (!_runtimeSession.IsRecording && !_runtimeSession.UseHookBasedPlaybackStop)
        {
            return KeyboardCaptureOutcome.None;
        }

        bool isRecordStopShortcut = _runtimeSession.IsRecording
            && _runtimeSession.RecordKeyboardActions
            && !capturedEvent.IsInjected
            && HotkeyService.IsShortcutTrigger(capturedEvent, recordHotkey, _runtimeSession.PressedPhysicalKeys);
        bool isPlaybackStopShortcut = _runtimeSession.IsPlaying
            && _runtimeSession.UseHookBasedPlaybackStop
            && !capturedEvent.IsInjected
            && HotkeyService.IsShortcutTrigger(capturedEvent, playHotkey, _runtimeSession.PressedPhysicalKeys);

        _runtimeSession.UpdatePressedPhysicalKeys(capturedEvent);

        if (isPlaybackStopShortcut)
        {
            return KeyboardCaptureOutcome.StopPlayback;
        }

        if (!_runtimeSession.IsRecording || capturedEvent.IsInjected)
        {
            return KeyboardCaptureOutcome.None;
        }

        if (isRecordStopShortcut)
        {
            return KeyboardCaptureOutcome.StopRecording;
        }

        _macroEventBuffer.AppendCapturedEvent(capturedEvent, _recordingStopwatch.Elapsed);
        return KeyboardCaptureOutcome.EventRecorded;
    }

    public PlaybackLaunchResult TryStartPlayback(int repeatCount, bool loopForever, double speedMultiplier, bool useStraightPaths)
    {
        if (!_runtimeSession.TryBeginPlayback())
        {
            return new PlaybackLaunchResult(false);
        }

        IReadOnlyList<MacroEvent> playbackEvents = useStraightPaths
            ? StraightPathService.TransformForPlayback(_macroEventBuffer.Events)
            : _macroEventBuffer.Events;
        bool useHookBasedPlaybackStop = MacroEventInspector.ContainsKeyboardEvents(playbackEvents);
        if (useHookBasedPlaybackStop)
        {
            try
            {
                _suspendHotkeys();
                _runtimeSession.EnableHookBasedPlaybackStop();
                _keyboardHookService.Start();
            }
            catch (Exception exception)
            {
                _runtimeSession.DisableHookBasedPlaybackStop();

                if (_keyboardHookService.IsRunning)
                {
                    _keyboardHookService.Stop();
                }

                _resumeHotkeys();
                _runtimeSession.CompletePlayback();
                return new PlaybackLaunchResult(false, Failure: exception);
            }
        }

        return new PlaybackLaunchResult(
            true,
            RunPlaybackAsync(playbackEvents, repeatCount, loopForever, speedMultiplier));
    }

    public void StopPlayback()
    {
        if (!_runtimeSession.IsPlaying)
        {
            return;
        }

        _runtimeSession.CancelPlayback();
    }

    private async Task<PlaybackRunResult> RunPlaybackAsync(
        IReadOnlyList<MacroEvent> playbackEvents,
        int repeatCount,
        bool loopForever,
        double speedMultiplier)
    {
        try
        {
            await _playbackService.PlayAsync(
                playbackEvents,
                repeatCount,
                loopForever,
                speedMultiplier,
                _runtimeSession.PlaybackCancellationToken);
            return new PlaybackRunResult(PlaybackRunStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            return new PlaybackRunResult(PlaybackRunStatus.Cancelled);
        }
        catch (Exception exception)
        {
            return new PlaybackRunResult(PlaybackRunStatus.Failed, exception);
        }
        finally
        {
            bool usedHookBasedPlaybackStop = _runtimeSession.CompletePlayback();
            if (usedHookBasedPlaybackStop)
            {
                if (_keyboardHookService.IsRunning)
                {
                    _keyboardHookService.Stop();
                }

                _resumeHotkeys();
            }
        }
    }
}
