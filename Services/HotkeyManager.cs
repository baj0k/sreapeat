using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal enum HotkeyCommand
{
    None,
    ToggleRecord,
    TogglePlay,
    ToggleLock,
}

internal sealed class HotkeyManager
{
    public const int RecordHotkeyId = 1001;
    public const int PlayHotkeyId = 1002;
    public const int LockHotkeyId = 1003;

    private readonly HashSet<string> _unavailableHotkeys = [];
    private readonly Func<nint, int, uint, uint, bool> _registerHotKey;
    private readonly Action<nint, int> _unregisterHotKey;

    public HotkeyManager(
        HotkeyBinding recordHotkey,
        HotkeyBinding playHotkey,
        HotkeyBinding lockHotkey,
        Func<nint, int, uint, uint, bool>? registerHotKey = null,
        Action<nint, int>? unregisterHotKey = null)
    {
        RecordHotkey = recordHotkey;
        PlayHotkey = playHotkey;
        LockHotkey = lockHotkey;
        _registerHotKey = registerHotKey ?? ((handle, id, modifiers, virtualKey) => NativeMethods.RegisterHotKey(handle, id, modifiers, virtualKey));
        _unregisterHotKey = unregisterHotKey ?? ((handle, id) => NativeMethods.UnregisterHotKey(handle, id));
    }

    public bool AreSuspended { get; private set; }

    public HotkeyBinding RecordHotkey { get; private set; }

    public HotkeyBinding PlayHotkey { get; private set; }

    public HotkeyBinding LockHotkey { get; private set; }

    public IReadOnlySet<string> UnavailableHotkeys => _unavailableHotkeys;

    public IReadOnlyList<string> RegisterAll(nint handle, bool resetUnavailableState = false)
    {
        if (resetUnavailableState)
        {
            _unavailableHotkeys.Clear();
        }

        AreSuspended = false;

        List<string> newlyUnavailable = [];
        TryRegister(handle, RecordHotkeyId, RecordHotkey, newlyUnavailable);
        TryRegister(handle, PlayHotkeyId, PlayHotkey, newlyUnavailable);
        TryRegister(handle, LockHotkeyId, LockHotkey, newlyUnavailable);
        return newlyUnavailable;
    }

    public IReadOnlyList<string> UpdateBindings(nint handle, HotkeyBinding recordHotkey, HotkeyBinding playHotkey)
    {
        UnregisterAll(handle);
        RecordHotkey = recordHotkey;
        PlayHotkey = playHotkey;
        return RegisterAll(handle, resetUnavailableState: true);
    }

    public void UnregisterAll(nint handle)
    {
        _unregisterHotKey(handle, RecordHotkeyId);
        _unregisterHotKey(handle, PlayHotkeyId);
        _unregisterHotKey(handle, LockHotkeyId);
    }

    public void Suspend(nint handle)
    {
        if (AreSuspended)
        {
            return;
        }

        UnregisterAll(handle);
        AreSuspended = true;
    }

    public IReadOnlyList<string> Resume(nint handle, bool resetUnavailableState = true)
    {
        if (!AreSuspended)
        {
            return [];
        }

        return RegisterAll(handle, resetUnavailableState);
    }

    public HotkeyCommand GetCommand(int msg, nint wParam)
    {
        if (msg != NativeMethods.WmHotKey || AreSuspended)
        {
            return HotkeyCommand.None;
        }

        return wParam.ToInt32() switch
        {
            RecordHotkeyId => HotkeyCommand.ToggleRecord,
            PlayHotkeyId => HotkeyCommand.TogglePlay,
            LockHotkeyId => HotkeyCommand.ToggleLock,
            _ => HotkeyCommand.None,
        };
    }

    public bool IsUnavailable(HotkeyBinding binding)
    {
        return _unavailableHotkeys.Contains(binding.DisplayText);
    }

    private void TryRegister(nint handle, int id, HotkeyBinding binding, List<string> newlyUnavailable)
    {
        if (_registerHotKey(handle, id, binding.Modifiers, binding.VirtualKey))
        {
            return;
        }

        if (_unavailableHotkeys.Add(binding.DisplayText))
        {
            newlyUnavailable.Add(binding.DisplayText);
        }
    }
}
