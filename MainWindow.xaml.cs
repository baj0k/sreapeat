using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Sreapeat.Helpers;
using Sreapeat.Models;
using Sreapeat.Services;

namespace Sreapeat;

public partial class MainWindow : Window
{
    private sealed record HotkeyBinding(string DisplayText, uint Modifiers, uint VirtualKey);

    private const int HotkeyRecord = 1001;
    private const int HotkeyPlay = 1002;
    private const double CompactWindowWidth = 356;
    private const double CompactWindowMinWidth = 348;
    private const double ExpandedWindowWidth = 548;
    private const double ExpandedWindowMinWidth = 540;
    private const double SettingsPaneOpenWidth = 188;
    private const int PaneAnimationMilliseconds = 140;

    private readonly MouseHookService _mouseHookService = new();
    private readonly PlaybackService _playbackService = new();
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly List<MacroEvent> _recordedEvents = [];

    private CancellationTokenSource? _playbackCancellationTokenSource;
    private readonly List<string> _unavailableHotkeys = [];
    private readonly HotkeyBinding _defaultRecordHotkey = CreateHotkeyBinding(Key.F5, ModifierKeys.None);
    private readonly HotkeyBinding _defaultPlayHotkey = CreateHotkeyBinding(Key.F6, ModifierKeys.None);
    private HwndSource? _windowSource;
    private TimeSpan _lastRecordedOffset = TimeSpan.Zero;
    private bool _isPlaying;
    private bool _isRecording;
    private bool _isUpdatingUi;
    private bool _isSettingsOpen;
    private HotkeyBinding _recordHotkey;
    private HotkeyBinding _playHotkey;
    private HotkeyBinding _draftRecordHotkey;
    private HotkeyBinding _draftPlayHotkey;

    public MainWindow()
    {
        InitializeComponent();

        _recordHotkey = _defaultRecordHotkey;
        _playHotkey = _defaultPlayHotkey;
        _draftRecordHotkey = _recordHotkey;
        _draftPlayHotkey = _playHotkey;

        _mouseHookService.ShouldIgnorePoint = IsPointInsideWindow;
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
        RefreshShortcutEditorText();
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

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSettingsPaneOpen(!_isSettingsOpen);
    }

    private void ShortcutTextBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        e.Handled = true;
        textBox.Focus();
    }

    private void ShortcutTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void RecordHotkeyTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureShortcut(e, isRecordField: true);
    }

    private void PlayHotkeyTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureShortcut(e, isRecordField: false);
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

        PlayButton.IsEnabled = !_isRecording && (_isPlaying || _recordedEvents.Count > 0);
        PlayIcon.Visibility = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
        StopIcon.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;

        LoopToggleButton.IsEnabled = !_isRecording && !_isPlaying;

        RepeatCountTextBox.IsEnabled = !_isRecording && !_isPlaying && LoopToggleButton.IsChecked != true;
        SpeedTextBox.IsEnabled = !_isRecording && !_isPlaying;

        RecordToggleButton.ToolTip = _isRecording ? $"Stop recording ({_recordHotkey.DisplayText})" : $"Start recording ({_recordHotkey.DisplayText})";
        PlayButton.ToolTip = _isPlaying ? $"Stop playback ({_playHotkey.DisplayText})" : $"Play current macro ({_playHotkey.DisplayText})";
        LoopToggleButton.ToolTip = LoopToggleButton.IsChecked == true ? "Loop is on" : "Loop is off";
        SettingsButton.ToolTip = _isSettingsOpen ? "Hide settings" : "Show settings";

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

        TryRegisterHotkey(handle, HotkeyRecord, _recordHotkey);
        TryRegisterHotkey(handle, HotkeyPlay, _playHotkey);
    }

    private void TryRegisterHotkey(nint handle, int id, HotkeyBinding binding)
    {
        bool succeeded = NativeMethods.RegisterHotKey(handle, id, binding.Modifiers, binding.VirtualKey);
        if (succeeded)
        {
            return;
        }

        _unavailableHotkeys.Add(binding.DisplayText);
        UpdateShortcutLegend();
    }

    private void UnregisterHotkeys()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, HotkeyRecord);
        NativeMethods.UnregisterHotKey(handle, HotkeyPlay);
    }

    private void SetSettingsPaneOpen(bool isOpen)
    {
        if (isOpen == _isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = isOpen;

        if (isOpen)
        {
            _draftRecordHotkey = _recordHotkey;
            _draftPlayHotkey = _playHotkey;
            RefreshShortcutEditorText();

            SettingsPane.Visibility = Visibility.Visible;
            SettingsPane.IsHitTestVisible = true;
            SettingsPaneColumn.Width = GridLength.Auto;
            MinWidth = ExpandedWindowMinWidth;

            AnimatePane(
                targetWidth: SettingsPaneOpenWidth,
                targetOpacity: 1.0,
                onCompleted: null);
            AnimateWindowWidth(ExpandedWindowWidth);

            _ = Dispatcher.InvokeAsync(() =>
            {
                RecordHotkeyTextBox.Focus();
                RecordHotkeyTextBox.SelectAll();
            });
        }
        else
        {
            if (RecordHotkeyTextBox.IsKeyboardFocusWithin || PlayHotkeyTextBox.IsKeyboardFocusWithin)
            {
                SettingsButton.Focus();
            }

            SettingsPane.IsHitTestVisible = false;
            MinWidth = CompactWindowMinWidth;

            AnimatePane(
                targetWidth: 0,
                targetOpacity: 0,
                onCompleted: () =>
                {
                    if (_isSettingsOpen)
                    {
                        return;
                    }

                    SettingsPane.Visibility = Visibility.Collapsed;
                    SettingsPaneColumn.Width = new GridLength(0);
                });
            AnimateWindowWidth(CompactWindowWidth);
        }

        UpdateUiState();
    }

    private void AnimatePane(double targetWidth, double targetOpacity, Action? onCompleted)
    {
        SettingsPane.BeginAnimation(WidthProperty, null);
        SettingsPane.BeginAnimation(OpacityProperty, null);

        DoubleAnimation widthAnimation = CreateAnimation(targetWidth);
        DoubleAnimation opacityAnimation = CreateAnimation(targetOpacity);

        if (onCompleted is not null)
        {
            widthAnimation.Completed += (_, _) => onCompleted();
        }

        SettingsPane.BeginAnimation(WidthProperty, widthAnimation);
        SettingsPane.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void AnimateWindowWidth(double targetWidth)
    {
        BeginAnimation(WidthProperty, null);
        BeginAnimation(WidthProperty, CreateAnimation(targetWidth));
    }

    private void RefreshShortcutEditorText()
    {
        RecordHotkeyTextBox.Text = _draftRecordHotkey.DisplayText;
        PlayHotkeyTextBox.Text = _draftPlayHotkey.DisplayText;
    }

    private void CaptureShortcut(KeyEventArgs e, bool isRecordField)
    {
        e.Handled = true;

        if (!TryCreateHotkeyBinding(e, out HotkeyBinding binding))
        {
            SetStatus("Shortcut must include a non-modifier key.");
            return;
        }

        if (isRecordField)
        {
            _draftRecordHotkey = binding;
            RecordHotkeyTextBox.Text = binding.DisplayText;
            RecordHotkeyTextBox.SelectAll();
        }
        else
        {
            _draftPlayHotkey = binding;
        }

        if (!TryApplyShortcutSettings())
        {
            if (isRecordField)
            {
                _draftRecordHotkey = _recordHotkey;
            }
            else
            {
                _draftPlayHotkey = _playHotkey;
            }

            RefreshShortcutEditorText();
        }

        TextBox targetTextBox = isRecordField ? RecordHotkeyTextBox : PlayHotkeyTextBox;
        targetTextBox.SelectAll();
    }

    private bool TryApplyShortcutSettings()
    {
        if (_draftRecordHotkey.VirtualKey == _draftPlayHotkey.VirtualKey
            && _draftRecordHotkey.Modifiers == _draftPlayHotkey.Modifiers)
        {
            SetStatus("Record and play shortcuts must be different.");
            return false;
        }

        UnregisterHotkeys();
        _unavailableHotkeys.Clear();
        _recordHotkey = _draftRecordHotkey;
        _playHotkey = _draftPlayHotkey;
        RegisterHotkeys();
        RefreshShortcutEditorText();
        UpdateShortcutLegend();
        UpdateUiState();
        SetStatus(_unavailableHotkeys.Count == 0 ? "Shortcuts updated." : "Some shortcuts are unavailable.");
        return true;
    }

    private void UpdateShortcutLegend()
    {
        string legend = $"{_recordHotkey.DisplayText} Record    {_playHotkey.DisplayText} Play/Stop";
        if (_unavailableHotkeys.Count > 0)
        {
            legend += $"    Unavailable: {string.Join(", ", _unavailableHotkeys)}";
        }

        ShortcutLegendTextBlock.Text = legend;
    }

    private bool IsPointInsideWindow(int x, int y)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || !NativeMethods.GetWindowRect(handle, out NativeMethods.Rect rect))
        {
            return false;
        }

        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
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

    private static bool TryCreateHotkeyBinding(KeyEventArgs e, out HotkeyBinding binding)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            binding = CreateHotkeyBinding(Key.F5, ModifierKeys.None);
            return false;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        binding = CreateHotkeyBinding(key, modifiers);
        return true;
    }

    private static HotkeyBinding CreateHotkeyBinding(Key key, ModifierKeys modifiers)
    {
        return new HotkeyBinding(
            FormatHotkeyText(modifiers, key),
            ConvertModifiers(modifiers),
            (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint nativeModifiers = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            nativeModifiers |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            nativeModifiers |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            nativeModifiers |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            nativeModifiers |= NativeMethods.ModWin;
        }

        return nativeModifiers;
    }

    private static string FormatHotkeyText(ModifierKeys modifiers, Key key)
    {
        List<string> parts = [];

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKeyLabel(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKeyLabel(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return key.ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + ((int)key - (int)Key.D0))).ToString();
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return key.ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return $"Num {(char)('0' + ((int)key - (int)Key.NumPad0))}";
        }

        return key switch
        {
            Key.Escape => "Esc",
            Key.Return => "Enter",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.Prior => "PgUp",
            Key.Next => "PgDn",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Snapshot => "PrintScreen",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem3 => "`",
            _ => ConvertKeyWithFallback(key),
        };
    }

    private static string ConvertKeyWithFallback(Key key)
    {
        KeyConverter converter = new();
        return converter.ConvertToString(key) as string ?? key.ToString();
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LWin
            or Key.RWin
            or Key.System;
    }

    private static DoubleAnimation CreateAnimation(double targetValue)
    {
        return new DoubleAnimation
        {
            To = targetValue,
            Duration = TimeSpan.FromMilliseconds(PaneAnimationMilliseconds),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
    }
}
