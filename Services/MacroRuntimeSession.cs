using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class MacroRuntimeSession : IDisposable
{
    private readonly HashSet<uint> _pressedPhysicalKeys = [];
    private CancellationTokenSource? _playbackCancellationTokenSource;

    public bool IsPlaying { get; private set; }

    public bool IsRecording { get; private set; }

    public bool RecordKeyboardActions { get; private set; }

    public bool UseHookBasedPlaybackStop { get; private set; }

    public IReadOnlySet<uint> PressedPhysicalKeys => _pressedPhysicalKeys;

    public CancellationToken PlaybackCancellationToken => _playbackCancellationTokenSource?.Token ?? CancellationToken.None;

    public bool TryBeginRecording(bool recordKeyboardActions)
    {
        if (IsRecording || IsPlaying)
        {
            return false;
        }

        RecordKeyboardActions = recordKeyboardActions;
        IsRecording = true;
        return true;
    }

    public bool StopRecording(out bool restoreHotkeys)
    {
        if (!IsRecording)
        {
            restoreHotkeys = false;
            return false;
        }

        restoreHotkeys = RecordKeyboardActions;
        IsRecording = false;
        RecordKeyboardActions = false;
        ClearPressedPhysicalKeys();
        return true;
    }

    public bool TryBeginPlayback()
    {
        if (IsRecording || IsPlaying)
        {
            return false;
        }

        _playbackCancellationTokenSource = new CancellationTokenSource();
        IsPlaying = true;
        return true;
    }

    public void EnableHookBasedPlaybackStop()
    {
        UseHookBasedPlaybackStop = true;
        ClearPressedPhysicalKeys();
    }

    public void DisableHookBasedPlaybackStop()
    {
        UseHookBasedPlaybackStop = false;
        ClearPressedPhysicalKeys();
    }

    public bool CompletePlayback()
    {
        if (!IsPlaying && _playbackCancellationTokenSource is null)
        {
            return false;
        }

        _playbackCancellationTokenSource?.Dispose();
        _playbackCancellationTokenSource = null;
        IsPlaying = false;

        bool usedHookBasedPlaybackStop = UseHookBasedPlaybackStop;
        UseHookBasedPlaybackStop = false;
        ClearPressedPhysicalKeys();
        return usedHookBasedPlaybackStop;
    }

    public void CancelPlayback()
    {
        _playbackCancellationTokenSource?.Cancel();
    }

    public void UpdatePressedPhysicalKeys(MacroEvent keyboardEvent)
    {
        if (keyboardEvent.IsInjected || keyboardEvent.Type is not (MacroEventType.KeyDown or MacroEventType.KeyUp))
        {
            return;
        }

        if (keyboardEvent.Type == MacroEventType.KeyDown)
        {
            _pressedPhysicalKeys.Add(keyboardEvent.VirtualKey);
            return;
        }

        _pressedPhysicalKeys.Remove(keyboardEvent.VirtualKey);
    }

    public void ClearPressedPhysicalKeys()
    {
        _pressedPhysicalKeys.Clear();
    }

    public void Dispose()
    {
        _playbackCancellationTokenSource?.Dispose();
        _playbackCancellationTokenSource = null;
        IsPlaying = false;
        IsRecording = false;
        RecordKeyboardActions = false;
        UseHookBasedPlaybackStop = false;
        ClearPressedPhysicalKeys();
    }
}
