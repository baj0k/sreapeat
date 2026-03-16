using System.Windows.Input;
using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class MacroCoordinatorTests
{
    [Fact]
    public void StartRecording_WithKeyboardCapture_StartsHooksAndResumesHotkeysOnStop()
    {
        MacroEventBuffer buffer = new();
        MacroRuntimeSession session = new();
        FakeHookService mouseHook = new();
        FakeHookService keyboardHook = new();
        FakePlaybackService playbackService = new();
        int suspendCount = 0;
        int resumeCount = 0;
        MacroCoordinator coordinator = new(
            buffer,
            session,
            mouseHook,
            keyboardHook,
            playbackService,
            () => suspendCount++,
            () => resumeCount++);

        bool started = coordinator.StartRecording(recordKeyboardActions: true);
        bool stopped = coordinator.StopRecording(HotkeyService.CreateBinding(Key.F5, ModifierKeys.None));

        Assert.True(started);
        Assert.True(stopped);
        Assert.Equal(1, suspendCount);
        Assert.Equal(1, resumeCount);
        Assert.Equal(1, mouseHook.StartCalls);
        Assert.Equal(1, mouseHook.StopCalls);
        Assert.Equal(1, keyboardHook.StartCalls);
        Assert.Equal(1, keyboardHook.StopCalls);
        Assert.False(session.IsRecording);
    }

    [Fact]
    public void StartRecording_WhenKeyboardHookFails_RestoresPreviousEventsAndHotkeys()
    {
        MacroEventBuffer buffer = new();
        buffer.ReplaceAll([new(MacroEventType.LeftDown, 10, 20, 0, TimeSpan.FromMilliseconds(5))]);

        MacroRuntimeSession session = new();
        FakeHookService mouseHook = new();
        FakeHookService keyboardHook = new() { ThrowOnStart = true };
        int suspendCount = 0;
        int resumeCount = 0;
        MacroCoordinator coordinator = new(
            buffer,
            session,
            mouseHook,
            keyboardHook,
            new FakePlaybackService(),
            () => suspendCount++,
            () => resumeCount++);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => coordinator.StartRecording(recordKeyboardActions: true));

        Assert.Equal("Hook start failed.", exception.Message);
        Assert.Equal(1, suspendCount);
        Assert.Equal(1, resumeCount);
        Assert.Equal(1, mouseHook.StartCalls);
        Assert.Equal(1, mouseHook.StopCalls);
        Assert.False(session.IsRecording);
        Assert.Single(buffer.Events);
        Assert.Equal(MacroEventType.LeftDown, buffer.Events[0].Type);
    }

    [Fact]
    public void HandleKeyboardCaptured_ReturnsStopRecordingWhenRecordShortcutIsPressed()
    {
        MacroEventBuffer buffer = new();
        MacroRuntimeSession session = new();
        MacroCoordinator coordinator = new(
            buffer,
            session,
            new FakeHookService(),
            new FakeHookService(),
            new FakePlaybackService(),
            static () => { },
            static () => { });
        HotkeyBinding recordHotkey = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Shift);
        HotkeyBinding playHotkey = HotkeyService.CreateBinding(Key.F6, ModifierKeys.None);

        coordinator.StartRecording(recordKeyboardActions: true);
        coordinator.HandleKeyboardCaptured(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x11), recordHotkey, playHotkey);
        coordinator.HandleKeyboardCaptured(new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x10), recordHotkey, playHotkey);

        KeyboardCaptureOutcome outcome = coordinator.HandleKeyboardCaptured(
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, recordHotkey.VirtualKey),
            recordHotkey,
            playHotkey);

        Assert.Equal(KeyboardCaptureOutcome.StopRecording, outcome);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public async Task TryStartPlayback_WithKeyboardEvents_UsesKeyboardHookAndResumesHotkeysOnCompletion()
    {
        MacroEventBuffer buffer = new();
        buffer.ReplaceAll([new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x41)]);

        MacroRuntimeSession session = new();
        FakeHookService keyboardHook = new();
        TaskCompletionSource playbackGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePlaybackService playbackService = new()
        {
            PlayAsyncHandler = _ => playbackGate.Task,
        };
        int suspendCount = 0;
        int resumeCount = 0;
        MacroCoordinator coordinator = new(
            buffer,
            session,
            new FakeHookService(),
            keyboardHook,
            playbackService,
            () => suspendCount++,
            () => resumeCount++);

        PlaybackLaunchResult launchResult = coordinator.TryStartPlayback(repeatCount: 1, loopForever: false, speedMultiplier: 1.0);

        Assert.True(launchResult.Started);
        Assert.NotNull(launchResult.PlaybackTask);
        Assert.True(session.IsPlaying);
        Assert.Equal(1, suspendCount);
        Assert.Equal(1, keyboardHook.StartCalls);

        playbackGate.SetResult();
        PlaybackRunResult playbackResult = await launchResult.PlaybackTask!;

        Assert.Equal(PlaybackRunStatus.Completed, playbackResult.Status);
        Assert.False(session.IsPlaying);
        Assert.Equal(1, keyboardHook.StopCalls);
        Assert.Equal(1, resumeCount);
    }

    [Fact]
    public void TryStartPlayback_WhenKeyboardHookFails_ReturnsFailureAndRestoresState()
    {
        MacroEventBuffer buffer = new();
        buffer.ReplaceAll([new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, 0x41)]);

        MacroRuntimeSession session = new();
        FakeHookService keyboardHook = new() { ThrowOnStart = true };
        int suspendCount = 0;
        int resumeCount = 0;
        MacroCoordinator coordinator = new(
            buffer,
            session,
            new FakeHookService(),
            keyboardHook,
            new FakePlaybackService(),
            () => suspendCount++,
            () => resumeCount++);

        PlaybackLaunchResult launchResult = coordinator.TryStartPlayback(repeatCount: 1, loopForever: false, speedMultiplier: 1.0);

        Assert.False(launchResult.Started);
        Assert.NotNull(launchResult.Failure);
        Assert.Equal("Hook start failed.", launchResult.Failure!.Message);
        Assert.False(session.IsPlaying);
        Assert.Equal(1, suspendCount);
        Assert.Equal(1, resumeCount);
    }

    private sealed class FakeHookService : IHookService
    {
        public bool ThrowOnStart { get; init; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public bool IsRunning { get; private set; }

        public void Start()
        {
            StartCalls++;
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Hook start failed.");
            }

            IsRunning = true;
        }

        public void Stop()
        {
            StopCalls++;
            IsRunning = false;
        }
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public int Calls { get; private set; }

        public Func<CancellationToken, Task>? PlayAsyncHandler { get; init; }

        public Task PlayAsync(
            IReadOnlyList<MacroEvent> events,
            int repeatCount,
            bool loopForever,
            double speedMultiplier,
            CancellationToken cancellationToken)
        {
            Calls++;
            return PlayAsyncHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
