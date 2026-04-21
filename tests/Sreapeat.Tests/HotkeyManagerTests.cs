using System.Windows.Input;
using Sreapeat.Helpers;
using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class HotkeyManagerTests
{
    [Fact]
    public void RegisterAll_TracksUnavailableBindings()
    {
        HotkeyBinding recordHotkey = HotkeyService.CreateBinding(Key.F5, ModifierKeys.None);
        HotkeyBinding playHotkey = HotkeyService.CreateBinding(Key.F6, ModifierKeys.None);
        HotkeyBinding lockHotkey = HotkeyService.CreateBinding(Key.F9, ModifierKeys.None);
        List<int> attemptedIds = [];

        HotkeyManager manager = new(
            recordHotkey,
            playHotkey,
            lockHotkey,
            (handle, id, modifiers, virtualKey) =>
            {
                attemptedIds.Add(id);
                return id != HotkeyManager.PlayHotkeyId;
            },
            static (_, _) => { });

        IReadOnlyList<string> unavailable = manager.RegisterAll((nint)123, resetUnavailableState: true);

        Assert.Equal([HotkeyManager.RecordHotkeyId, HotkeyManager.PlayHotkeyId, HotkeyManager.LockHotkeyId], attemptedIds);
        Assert.Single(unavailable);
        Assert.Contains(playHotkey.DisplayText, unavailable);
        Assert.True(manager.IsUnavailable(playHotkey));
    }

    [Fact]
    public void SuspendAndResume_UnregistersAndRegistersHotkeys()
    {
        HotkeyBinding recordHotkey = HotkeyService.CreateBinding(Key.F5, ModifierKeys.None);
        HotkeyBinding playHotkey = HotkeyService.CreateBinding(Key.F6, ModifierKeys.None);
        HotkeyBinding lockHotkey = HotkeyService.CreateBinding(Key.F9, ModifierKeys.None);
        int registerCount = 0;
        int unregisterCount = 0;

        HotkeyManager manager = new(
            recordHotkey,
            playHotkey,
            lockHotkey,
            (handle, id, modifiers, virtualKey) =>
            {
                registerCount++;
                return true;
            },
            (handle, id) => unregisterCount++);

        manager.RegisterAll((nint)123, resetUnavailableState: true);
        manager.Suspend((nint)123);

        Assert.True(manager.AreSuspended);
        Assert.Equal(3, unregisterCount);

        manager.Resume((nint)123);

        Assert.False(manager.AreSuspended);
        Assert.Equal(6, registerCount);
    }

    [Fact]
    public void UpdateBindings_ReplacesCurrentBindingsAndClearsUnavailableState()
    {
        HotkeyBinding originalRecordHotkey = HotkeyService.CreateBinding(Key.F5, ModifierKeys.None);
        HotkeyBinding originalPlayHotkey = HotkeyService.CreateBinding(Key.F6, ModifierKeys.None);
        HotkeyBinding lockHotkey = HotkeyService.CreateBinding(Key.F9, ModifierKeys.None);
        HotkeyBinding updatedPlayHotkey = HotkeyService.CreateBinding(Key.P, ModifierKeys.Control);

        HotkeyManager manager = new(
            originalRecordHotkey,
            originalPlayHotkey,
            lockHotkey,
            (handle, id, modifiers, virtualKey) => virtualKey != originalPlayHotkey.VirtualKey,
            static (_, _) => { });

        manager.RegisterAll((nint)123, resetUnavailableState: true);
        manager.UpdateBindings((nint)123, originalRecordHotkey, updatedPlayHotkey);

        Assert.Equal(updatedPlayHotkey, manager.PlayHotkey);
        Assert.DoesNotContain(originalPlayHotkey.DisplayText, manager.UnavailableHotkeys);
    }

    [Fact]
    public void GetCommand_ReturnsExpectedAction()
    {
        HotkeyManager manager = new(
            HotkeyService.CreateBinding(Key.F5, ModifierKeys.None),
            HotkeyService.CreateBinding(Key.F6, ModifierKeys.None),
            HotkeyService.CreateBinding(Key.F9, ModifierKeys.None),
            (handle, id, modifiers, virtualKey) => true,
            static (_, _) => { });

        HotkeyCommand recordCommand = manager.GetCommand(NativeMethods.WmHotKey, (nint)HotkeyManager.RecordHotkeyId);
        HotkeyCommand playCommand = manager.GetCommand(NativeMethods.WmHotKey, (nint)HotkeyManager.PlayHotkeyId);
        manager.Suspend((nint)123);
        HotkeyCommand suspendedCommand = manager.GetCommand(NativeMethods.WmHotKey, (nint)HotkeyManager.RecordHotkeyId);

        Assert.Equal(HotkeyCommand.ToggleRecord, recordCommand);
        Assert.Equal(HotkeyCommand.TogglePlay, playCommand);
        Assert.Equal(HotkeyCommand.None, suspendedCommand);
        Assert.Equal(HotkeyCommand.None, manager.GetCommand(0, (nint)HotkeyManager.RecordHotkeyId));
    }
}
