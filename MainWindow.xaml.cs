using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Sreapeat.Helpers;
using Sreapeat.Models;
using Sreapeat.Services;

namespace Sreapeat;

public partial class MainWindow : Window
{
    private const int HotkeyRecord = 1001;
    private const int HotkeyPlay = 1002;
    private const uint VirtualKeyF5 = 0x74;
    private const uint VirtualKeyF6 = 0x75;

    private readonly MouseHookService _mouseHookService = new();
    private readonly PlaybackService _playbackService = new();
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly List<MacroEvent> _recordedEvents = [];

    private CancellationTokenSource? _playbackCancellationTokenSource;
    private readonly List<string> _unavailableHotkeys = [];
    private HwndSource? _windowSource;
    private TimeSpan _lastRecordedOffset = TimeSpan.Zero;
    private bool _isPlaying;
    private bool _isRecording;
    private bool _isUpdatingUi;

    public MainWindow()
    {
        InitializeComponent();
        _mouseHookService.MouseActionCaptured += MouseHookService_OnMouseActionCaptured;
        SourceInitialized += MainWindow_OnSourceInitialized;
        Loaded += MainWindow_OnLoaded;
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        _windowSource?.AddHook(WndProc);
        RegisterHotkeys();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        UpdateShortcutLegend();
        UpdateEventCount();
        UpdateUiState();
        SetStatus("Ready...");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopPlayback();

        if (_isRecording)
        {
            StopRecording();
        }

        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WndProc);
        }

        UnregisterHotkeys();
        _mouseHookService.Dispose();

        base.OnClosing(e);
    }

    private void RecordToggleButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        RunSafeUiAction(StartRecording);
    }

    private void RecordToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        RunSafeUiAction(StopRecording);
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        await StartPlaybackAsync();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RecordMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            RunSafeUiAction(StopRecording);
            return;
        }

        RunSafeUiAction(StartRecording);
    }

    private async void PlayMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        await StartPlaybackAsync();
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "sreapeat\nF5 toggles recording.\nF6 plays or stops playback.",
            "About sreapeat",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LoopToggleButton_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        UpdateUiState();
    }

    private void MouseHookService_OnMouseActionCaptured(object? sender, MacroEvent capturedEvent)
    {
        if (!_isRecording)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_isRecording)
            {
                return;
            }

            TimeSpan currentOffset = _recordingStopwatch.Elapsed;
            TimeSpan delay = currentOffset - _lastRecordedOffset;
            _lastRecordedOffset = currentOffset;

            _recordedEvents.Add(capturedEvent with { DelayBeforeEvent = delay });
            UpdateEventCount();
        });
    }

    private void StartRecording()
    {
        if (_isRecording || _isPlaying)
        {
            return;
        }

        _recordedEvents.Clear();
        _lastRecordedOffset = TimeSpan.Zero;
        _recordingStopwatch.Restart();
        UpdateEventCount();

        _mouseHookService.Start();
        _isRecording = true;

        UpdateUiState();
        SetStatus("Recording...");
    }

    private void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _mouseHookService.Stop();
        _recordingStopwatch.Stop();
        _isRecording = false;

        UpdateUiState();
        SetStatus(_recordedEvents.Count == 0 ? "Ready..." : "Recording saved.");
    }

    private async Task StartPlaybackAsync()
    {
        if (_isPlaying || _isRecording)
        {
            return;
        }

        if (_recordedEvents.Count == 0)
        {
            SetStatus("Nothing to play.");
            return;
        }

        bool loopForever = LoopToggleButton.IsChecked == true;
        if (!TryGetRepeatCount(out int repeatCount))
        {
            MessageBox.Show(
                "Repeat count must be a whole number greater than zero.",
                "sreapeat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryGetSpeedMultiplier(out double speedMultiplier))
        {
            MessageBox.Show(
                "Speed must be a number between 0.10 and 10.00.",
                "sreapeat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _playbackCancellationTokenSource = new CancellationTokenSource();
        _isPlaying = true;
        UpdateUiState();
        SetStatus(loopForever
            ? $"Playing in loop at {speedMultiplier:0.##}x..."
            : $"Playing {repeatCount} time(s) at {speedMultiplier:0.##}x...");

        bool cancelled = false;

        try
        {
            await _playbackService.PlayAsync(
                _recordedEvents,
                repeatCount,
                loopForever,
                speedMultiplier,
                _playbackCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            _playbackCancellationTokenSource?.Dispose();
            _playbackCancellationTokenSource = null;
            _isPlaying = false;
            UpdateUiState();
            SetStatus(cancelled ? "Playback stopped." : "Ready...");
        }
    }

    private void StopPlayback()
    {
        if (!_isPlaying)
        {
            return;
        }

        _playbackCancellationTokenSource?.Cancel();
    }

    private void UpdateUiState()
    {
        _isUpdatingUi = true;

        RecordToggleButton.IsEnabled = !_isPlaying;
        RecordToggleButton.IsChecked = _isRecording;
        RecordButtonLabel.Text = _isRecording ? "Stop" : "Record";

        PlayButton.IsEnabled = !_isRecording && (_isPlaying || _recordedEvents.Count > 0);
        PlayButtonLabel.Text = _isPlaying ? "Stop" : "Play";

        LoopToggleButton.IsEnabled = !_isRecording && !_isPlaying;
        LoopButtonLabel.Text = LoopToggleButton.IsChecked == true ? "Loop On" : "Loop Off";

        RepeatCountTextBox.IsEnabled = !_isRecording && !_isPlaying && LoopToggleButton.IsChecked != true;
        SpeedTextBox.IsEnabled = !_isRecording && !_isPlaying;

        _isUpdatingUi = false;
    }

    private bool TryGetRepeatCount(out int repeatCount)
    {
        if (LoopToggleButton.IsChecked == true)
        {
            repeatCount = 1;
            return true;
        }

        return int.TryParse(RepeatCountTextBox.Text, out repeatCount) && repeatCount > 0;
    }

    private bool TryGetSpeedMultiplier(out double speedMultiplier)
    {
        bool parsed = double.TryParse(
                SpeedTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out speedMultiplier)
            || double.TryParse(
                SpeedTextBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out speedMultiplier);

        return parsed && speedMultiplier >= 0.10 && speedMultiplier <= 10.0;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotKey)
        {
            return nint.Zero;
        }

        int hotkeyId = wParam.ToInt32();
        switch (hotkeyId)
        {
            case HotkeyRecord:
                if (_isRecording)
                {
                    StopRecording();
                }
                else
                {
                    StartRecording();
                }

                handled = true;
                break;
            case HotkeyPlay:
                if (_isPlaying)
                {
                    StopPlayback();
                }
                else if (!_isRecording)
                {
                    _ = StartPlaybackAsync();
                }

                handled = true;
                break;
        }

        return nint.Zero;
    }

    private void RegisterHotkeys()
    {
        nint handle = new WindowInteropHelper(this).Handle;

        TryRegisterHotkey(handle, HotkeyRecord, 0, VirtualKeyF5, "F5");
        TryRegisterHotkey(handle, HotkeyPlay, 0, VirtualKeyF6, "F6");
    }

    private void TryRegisterHotkey(nint handle, int id, uint modifiers, uint virtualKey, string label)
    {
        bool succeeded = NativeMethods.RegisterHotKey(handle, id, modifiers, virtualKey);
        if (succeeded)
        {
            return;
        }

        _unavailableHotkeys.Add(label);
        UpdateShortcutLegend();
    }

    private void UnregisterHotkeys()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, HotkeyRecord);
        NativeMethods.UnregisterHotKey(handle, HotkeyPlay);
    }

    private void UpdateShortcutLegend()
    {
        string legend = "F5 record on/off | F6 play or stop";
        if (_unavailableHotkeys.Count > 0)
        {
            legend += $" | Unavailable: {string.Join(", ", _unavailableHotkeys)}";
        }

        ShortcutLegendTextBlock.Text = legend;
    }

    private void UpdateEventCount()
    {
        EventCountTextBlock.Text = _recordedEvents.Count == 1
            ? "1 event"
            : $"{_recordedEvents.Count} events";
    }

    private void SetStatus(string status)
    {
        StatusTextBlock.Text = status;
    }

    private void RunSafeUiAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "sreapeat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateUiState();
        }
    }
}
