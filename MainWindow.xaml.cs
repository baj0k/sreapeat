using System.ComponentModel;
using System.Globalization;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using Sreapeat.Helpers;
using Sreapeat.Models;
using Sreapeat.Services;

namespace Sreapeat;

public partial class MainWindow : Window
{
    private const string AppTitle = "sreapeat";
    private const string PlaybackErrorTitle = "sreapeat playback error";
    private const string MacroFileFilter = "sreapeat macro (*.sreapeat.json)|*.sreapeat.json|JSON files (*.json)|*.json|All files (*.*)|*.*";
    private const string MacroFileExtension = ".json";
    private const string DefaultExportFileName = "macro.sreapeat.json";
    private const double CompactWindowWidth = 356;
    private const double CompactWindowMinWidth = 348;
    private const double CompactWindowHeight = 136;
    private const double CompactWindowMinHeight = 132;
    private const double ExpandedWindowWidth = 548;
    private const double ExpandedWindowMinWidth = 540;
    private const double ExpandedWindowHeight = 166;
    private const double ExpandedWindowMinHeight = 162;
    private const double SettingsPaneOpenWidth = 188;
    private const int PaneAnimationMilliseconds = 140;

    private readonly KeyboardHookService _keyboardHookService = new();
    private readonly MacroEventBuffer _macroEventBuffer = new();
    private readonly MouseHookService _mouseHookService = new();
    private readonly MacroFileService _macroFileService = new();
    private readonly HotkeyManager _hotkeyManager;
    private readonly MacroCoordinator _macroCoordinator;
    private readonly MacroRuntimeSession _runtimeSession = new();

    private readonly HotkeyBinding _defaultRecordHotkey = HotkeyService.CreateBinding(Key.F5, ModifierKeys.None);
    private readonly HotkeyBinding _defaultPlayHotkey = HotkeyService.CreateBinding(Key.F6, ModifierKeys.None);
    private readonly Brush _defaultShortcutBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B6559"));
    private readonly Brush _unavailableShortcutBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A14D4D"));
    private HwndSource? _windowSource;
    private bool _isUpdatingUi;
    private bool _isSettingsOpen;
    private bool _isClosing;
    private HotkeyBinding _draftRecordHotkey;
    private HotkeyBinding _draftPlayHotkey;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowMetrics(isExpanded: false, animate: false);

        _hotkeyManager = new HotkeyManager(_defaultRecordHotkey, _defaultPlayHotkey);
        _macroCoordinator = new MacroCoordinator(
            _macroEventBuffer,
            _runtimeSession,
            _mouseHookService,
            _keyboardHookService,
            new PlaybackService(),
            SuspendHotkeys,
            ResumeHotkeys);
        _draftRecordHotkey = _hotkeyManager.RecordHotkey;
        _draftPlayHotkey = _hotkeyManager.PlayHotkey;
        _defaultShortcutBrush.Freeze();
        _unavailableShortcutBrush.Freeze();

        _keyboardHookService.KeyboardActionCaptured += KeyboardHookService_OnKeyboardActionCaptured;
        _mouseHookService.ShouldIgnorePoint = IsPointInsideWindow;
        _mouseHookService.MouseActionCaptured += MouseHookService_OnMouseActionCaptured;
        SourceInitialized += MainWindow_OnSourceInitialized;
        Loaded += MainWindow_OnLoaded;
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        _windowSource?.AddHook(WndProc);
        RegisterHotkeys(resetUnavailableState: true);
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
        SetStatus(_hotkeyManager.UnavailableHotkeys.Count == 0
            ? "Ready..."
            : $"Hotkey unavailable: {string.Join(", ", _hotkeyManager.UnavailableHotkeys)}");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        StopPlayback();

        if (_runtimeSession.IsRecording)
        {
            StopRecording();
        }

        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WndProc);
        }

        UnregisterHotkeys();
        _runtimeSession.Dispose();
        _keyboardHookService.Dispose();
        _mouseHookService.Dispose();

        base.OnClosing(e);
    }

    private void RecordToggleButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        RunSafeUiAction("starting recording", StartRecording);
    }

    private void RecordToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        RunSafeUiAction("stopping recording", () => StopRecording());
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_runtimeSession.IsPlaying)
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

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        if (_macroEventBuffer.HasEvents
            && ShowAppMessage(
                "Importing a macro replaces the current recorded events. Continue?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        string? filePath = SelectImportFile();
        if (filePath is null)
        {
            return;
        }

        RunSafeUiAction("importing a macro", () =>
        {
            IReadOnlyList<MacroEvent> importedEvents = _macroFileService.Import(filePath);
            _macroEventBuffer.ReplaceAll(importedEvents);
            UpdateEventCount();
            UpdateUiState();
            SetStatus($"Imported {FormatEventCountLabel()}.");
        });
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !_macroEventBuffer.HasEvents)
        {
            return;
        }

        string? filePath = SelectExportFile();
        if (filePath is null)
        {
            return;
        }

        RunSafeUiAction("exporting a macro", () =>
        {
            _macroFileService.Export(filePath, _macroEventBuffer.Events);
            SetStatus($"Exported {FormatEventCountLabel()}.");
        });
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
        if (!_runtimeSession.IsRecording)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_macroCoordinator.TryRecordMouseEvent(capturedEvent))
            {
                UpdateEventCount();
            }
        });
    }

    private void KeyboardHookService_OnKeyboardActionCaptured(object? sender, MacroEvent capturedEvent)
    {
        if (!_runtimeSession.IsRecording && !_runtimeSession.UseHookBasedPlaybackStop)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            switch (_macroCoordinator.HandleKeyboardCaptured(capturedEvent, _hotkeyManager.RecordHotkey, _hotkeyManager.PlayHotkey))
            {
                case KeyboardCaptureOutcome.StopPlayback:
                    StopPlayback();
                    break;
                case KeyboardCaptureOutcome.StopRecording:
                    StopRecording(trimTrailingShortcutEvents: true);
                    break;
                case KeyboardCaptureOutcome.EventRecorded:
                    UpdateEventCount();
                    break;
            }
        });
    }

    private void StartRecording()
    {
        bool recordKeyboardActions = RecordKeyboardCheckBox.IsChecked == true;
        if (!_macroCoordinator.StartRecording(recordKeyboardActions))
        {
            return;
        }
        UpdateEventCount();
        UpdateUiState();
        SetStatus(recordKeyboardActions ? "Recording mouse + keyboard..." : "Recording...");
    }

    private void StopRecording(bool trimTrailingShortcutEvents = false)
    {
        if (!_macroCoordinator.StopRecording(_hotkeyManager.RecordHotkey, trimTrailingShortcutEvents))
        {
            return;
        }

        UpdateUiState();
        SetStatus(_macroEventBuffer.Count == 0 ? "Ready..." : "Recording saved.");
    }

    private async Task StartPlaybackAsync()
    {
        if (!_macroEventBuffer.HasEvents)
        {
            SetStatus("Nothing to play.");
            return;
        }

        bool loopForever = LoopToggleButton.IsChecked == true;
        if (!TryGetRepeatCount(out int repeatCount))
        {
            ShowAppMessage(
                "Repeat count must be a whole number greater than zero.",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryGetSpeedMultiplier(out double speedMultiplier))
        {
            ShowAppMessage(
                "Speed must be a number between 0.10 and 10.00.",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool useStraightPaths = StraightPathsCheckBox.IsChecked == true;
        PlaybackLaunchResult launchResult = _macroCoordinator.TryStartPlayback(repeatCount, loopForever, speedMultiplier, useStraightPaths);
        if (launchResult.Failure is not null)
        {
            AppLogger.Error("Unable to prepare playback keyboard hook.", launchResult.Failure);
            ShowAppMessage(
                launchResult.Failure.Message,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Playback failed.");
            return;
        }

        if (!launchResult.Started || launchResult.PlaybackTask is null)
        {
            return;
        }

        UpdateUiState();

        string straightPathSuffix = useStraightPaths ? " with straight paths" : string.Empty;
        SetStatus(loopForever
            ? $"Playing in loop at {speedMultiplier:0.##}x{straightPathSuffix}..."
            : $"Playing {repeatCount} time(s) at {speedMultiplier:0.##}x{straightPathSuffix}...");

        PlaybackRunResult playbackResult = await launchResult.PlaybackTask;
        UpdateUiState();

        if (playbackResult.Status == PlaybackRunStatus.Failed && playbackResult.Failure is not null)
        {
            AppLogger.Error("Playback failed.", playbackResult.Failure);
            ShowAppMessage(
                playbackResult.Failure.Message,
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                PlaybackErrorTitle);
            SetStatus("Playback failed.");
            return;
        }

        SetStatus(playbackResult.Status == PlaybackRunStatus.Cancelled ? "Playback stopped." : "Ready...");
    }

    private void StopPlayback()
    {
        if (!_runtimeSession.IsPlaying)
        {
            return;
        }

        _macroCoordinator.StopPlayback();
    }

    private void UpdateUiState()
    {
        _isUpdatingUi = true;

        RecordToggleButton.IsEnabled = !_runtimeSession.IsPlaying;
        RecordToggleButton.IsChecked = _runtimeSession.IsRecording;
        RecordIcon.Visibility = _runtimeSession.IsRecording ? Visibility.Collapsed : Visibility.Visible;
        RecordStopIcon.Visibility = _runtimeSession.IsRecording ? Visibility.Visible : Visibility.Collapsed;

        PlayButton.IsEnabled = !_runtimeSession.IsRecording && (_runtimeSession.IsPlaying || _macroEventBuffer.HasEvents);
        PlayIcon.Visibility = _runtimeSession.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        StopIcon.Visibility = _runtimeSession.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        LoopToggleButton.IsEnabled = !_runtimeSession.IsRecording && !_runtimeSession.IsPlaying;

        RepeatCountTextBox.IsEnabled = !_runtimeSession.IsRecording && !_runtimeSession.IsPlaying && LoopToggleButton.IsChecked != true;
        SpeedTextBox.IsEnabled = !_runtimeSession.IsRecording && !_runtimeSession.IsPlaying;
        RecordKeyboardCheckBox.IsEnabled = !IsBusy;
        StraightPathsCheckBox.IsEnabled = !IsBusy;
        ImportButton.IsEnabled = !IsBusy;
        ExportButton.IsEnabled = !IsBusy && _macroEventBuffer.HasEvents;

        RecordToggleButton.ToolTip = _runtimeSession.IsRecording ? $"Stop recording ({_hotkeyManager.RecordHotkey.DisplayText})" : $"Start recording ({_hotkeyManager.RecordHotkey.DisplayText})";
        PlayButton.ToolTip = _runtimeSession.IsPlaying ? $"Stop playback ({_hotkeyManager.PlayHotkey.DisplayText})" : $"Play current macro ({_hotkeyManager.PlayHotkey.DisplayText})";
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
        if (msg == NativeMethods.WmHotKey && _hotkeyManager.AreSuspended)
        {
            handled = true;
            return nint.Zero;
        }

        switch (_hotkeyManager.GetCommand(msg, wParam))
        {
            case HotkeyCommand.ToggleRecord:
                if (_runtimeSession.IsRecording)
                {
                    StopRecording(trimTrailingShortcutEvents: false);
                }
                else
                {
                    StartRecording();
                }

                handled = true;
                break;
            case HotkeyCommand.TogglePlay:
                if (_runtimeSession.IsPlaying)
                {
                    StopPlayback();
                }
                else if (!_runtimeSession.IsRecording)
                {
                    _ = StartPlaybackAsync();
                }

                handled = true;
                break;
        }

        return nint.Zero;
    }

    private void RegisterHotkeys(bool resetUnavailableState = false)
    {
        IReadOnlyList<string> unavailableHotkeys = _hotkeyManager.RegisterAll(GetWindowHandle(), resetUnavailableState);
        LogUnavailableHotkeys(unavailableHotkeys);
        UpdateShortcutLegend();
    }

    private void UnregisterHotkeys()
    {
        _hotkeyManager.UnregisterAll(GetWindowHandle());
    }

    private void SuspendHotkeys()
    {
        _hotkeyManager.Suspend(GetWindowHandle());
    }

    private void ResumeHotkeys()
    {
        if (!_hotkeyManager.AreSuspended)
        {
            return;
        }

        if (_isClosing)
        {
            return;
        }

        IReadOnlyList<string> unavailableHotkeys = _hotkeyManager.Resume(GetWindowHandle(), resetUnavailableState: true);
        LogUnavailableHotkeys(unavailableHotkeys);
        UpdateShortcutLegend();
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
            _draftRecordHotkey = _hotkeyManager.RecordHotkey;
            _draftPlayHotkey = _hotkeyManager.PlayHotkey;
            RefreshShortcutEditorText();

            SettingsPane.Visibility = Visibility.Visible;
            SettingsPane.IsHitTestVisible = true;
            SettingsPaneColumn.Width = GridLength.Auto;

            AnimatePane(
                targetWidth: SettingsPaneOpenWidth,
                targetOpacity: 1.0,
                onCompleted: null);
            ApplyWindowMetrics(isExpanded: true, animate: true);

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
            ApplyWindowMetrics(isExpanded: false, animate: true);
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

    private void AnimateWindowSize(double targetWidth, double targetHeight)
    {
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(WidthProperty, CreateAnimation(targetWidth));
        BeginAnimation(HeightProperty, CreateAnimation(targetHeight));
    }

    private void ApplyWindowMetrics(bool isExpanded, bool animate)
    {
        double targetWidth = isExpanded ? ExpandedWindowWidth : CompactWindowWidth;
        double targetHeight = isExpanded ? ExpandedWindowHeight : CompactWindowHeight;

        MinWidth = isExpanded ? ExpandedWindowMinWidth : CompactWindowMinWidth;
        MinHeight = isExpanded ? ExpandedWindowMinHeight : CompactWindowMinHeight;

        if (animate)
        {
            AnimateWindowSize(targetWidth, targetHeight);
            return;
        }

        Width = targetWidth;
        Height = targetHeight;
    }

    private void RefreshShortcutEditorText()
    {
        RecordHotkeyTextBox.Text = _draftRecordHotkey.DisplayText;
        PlayHotkeyTextBox.Text = _draftPlayHotkey.DisplayText;
    }

    private void CaptureShortcut(KeyEventArgs e, bool isRecordField)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!HotkeyService.TryCreateBinding(key, Keyboard.Modifiers, out HotkeyBinding binding))
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
                _draftRecordHotkey = _hotkeyManager.RecordHotkey;
            }
            else
            {
                _draftPlayHotkey = _hotkeyManager.PlayHotkey;
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

        IReadOnlyList<string> unavailableHotkeys = _hotkeyManager.UpdateBindings(GetWindowHandle(), _draftRecordHotkey, _draftPlayHotkey);
        LogUnavailableHotkeys(unavailableHotkeys);
        RefreshShortcutEditorText();
        UpdateShortcutLegend();
        UpdateUiState();
        SetStatus(_hotkeyManager.UnavailableHotkeys.Count == 0 ? "Shortcuts updated." : "Some shortcuts are unavailable.");
        return true;
    }

    private void UpdateShortcutLegend()
    {
        bool recordUnavailable = _hotkeyManager.IsUnavailable(_hotkeyManager.RecordHotkey);
        bool playUnavailable = _hotkeyManager.IsUnavailable(_hotkeyManager.PlayHotkey);

        RecordShortcutTextBlock.Text = recordUnavailable
            ? $"{_hotkeyManager.RecordHotkey.DisplayText}*"
            : _hotkeyManager.RecordHotkey.DisplayText;
        PlayShortcutTextBlock.Text = playUnavailable
            ? $"{_hotkeyManager.PlayHotkey.DisplayText}*"
            : _hotkeyManager.PlayHotkey.DisplayText;
        RecordShortcutTextBlock.Foreground = recordUnavailable ? _unavailableShortcutBrush : _defaultShortcutBrush;
        PlayShortcutTextBlock.Foreground = playUnavailable ? _unavailableShortcutBrush : _defaultShortcutBrush;
    }

    private nint GetWindowHandle()
    {
        return new WindowInteropHelper(this).Handle;
    }

    private static void LogUnavailableHotkeys(IEnumerable<string> unavailableHotkeys)
    {
        foreach (string hotkey in unavailableHotkeys)
        {
            AppLogger.Warning($"Unable to register hotkey '{hotkey}'.");
        }
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
        EventCountTextBlock.Text = FormatEventCountLabel();
    }

    private void SetStatus(string status)
    {
        StatusTextBlock.Text = status;
    }

    private bool IsBusy => _runtimeSession.IsRecording || _runtimeSession.IsPlaying;

    private string FormatEventCountLabel()
    {
        return _macroEventBuffer.CountLabel;
    }

    private string? SelectImportFile()
    {
        OpenFileDialog openFileDialog = new()
        {
            Title = "Import macro",
            Filter = MacroFileFilter,
            DefaultExt = MacroFileExtension,
            CheckFileExists = true,
        };

        return openFileDialog.ShowDialog(this) == true
            ? openFileDialog.FileName
            : null;
    }

    private string? SelectExportFile()
    {
        SaveFileDialog saveFileDialog = new()
        {
            Title = "Export macro",
            Filter = MacroFileFilter,
            DefaultExt = MacroFileExtension,
            AddExtension = true,
            FileName = DefaultExportFileName,
            OverwritePrompt = true,
        };

        return saveFileDialog.ShowDialog(this) == true
            ? saveFileDialog.FileName
            : null;
    }

    private MessageBoxResult ShowAppMessage(
        string message,
        MessageBoxButton buttons,
        MessageBoxImage image,
        string title = AppTitle)
    {
        return MessageBox.Show(this, message, title, buttons, image);
    }

    private void RunSafeUiAction(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Failed while {operation}.", exception);
            ShowAppMessage(
                exception.Message,
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateUiState();
        }
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
