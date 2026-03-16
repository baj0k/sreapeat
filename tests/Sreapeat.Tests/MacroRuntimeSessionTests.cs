using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class MacroRuntimeSessionTests
{
    [Fact]
    public void TryBeginRecording_WithKeyboardCapture_SetsRecordingStateAndRestoresHotkeysOnStop()
    {
        MacroRuntimeSession session = new();

        bool started = session.TryBeginRecording(recordKeyboardActions: true);
        session.UpdatePressedPhysicalKeys(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x41));
        bool stopped = session.StopRecording(out bool restoreHotkeys);

        Assert.True(started);
        Assert.True(stopped);
        Assert.True(restoreHotkeys);
        Assert.False(session.IsRecording);
        Assert.False(session.RecordKeyboardActions);
        Assert.Empty(session.PressedPhysicalKeys);
    }

    [Fact]
    public void TryBeginPlayback_WithHookStop_ClearsStateOnComplete()
    {
        MacroRuntimeSession session = new();

        bool started = session.TryBeginPlayback();
        session.EnableHookBasedPlaybackStop();
        session.UpdatePressedPhysicalKeys(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x41));

        bool usedHookBasedStop = session.CompletePlayback();

        Assert.True(started);
        Assert.True(usedHookBasedStop);
        Assert.False(session.IsPlaying);
        Assert.False(session.UseHookBasedPlaybackStop);
        Assert.False(session.PlaybackCancellationToken.CanBeCanceled);
        Assert.Empty(session.PressedPhysicalKeys);
    }

    [Fact]
    public void UpdatePressedPhysicalKeys_TracksPhysicalKeysAndIgnoresInjectedEvents()
    {
        MacroRuntimeSession session = new();

        session.UpdatePressedPhysicalKeys(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x11));
        session.UpdatePressedPhysicalKeys(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x52, IsInjected: true));
        session.UpdatePressedPhysicalKeys(new(MacroEventType.KeyUp, 0, 0, 0, TimeSpan.Zero, 0x11));

        Assert.DoesNotContain(0x11u, session.PressedPhysicalKeys);
        Assert.DoesNotContain(0x52u, session.PressedPhysicalKeys);
    }

    [Fact]
    public void CancelPlayback_CancelsCurrentToken()
    {
        MacroRuntimeSession session = new();

        bool started = session.TryBeginPlayback();
        CancellationToken token = session.PlaybackCancellationToken;
        session.CancelPlayback();

        Assert.True(started);
        Assert.True(token.CanBeCanceled);
        Assert.True(token.IsCancellationRequested);
    }
}
