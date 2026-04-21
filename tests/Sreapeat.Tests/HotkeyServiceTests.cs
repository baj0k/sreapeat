using System.Windows.Input;
using Sreapeat.Helpers;
using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class HotkeyServiceTests
{
    [Fact]
    public void CreateBinding_FormatsDisplayTextAndNativeModifiers()
    {
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal("Ctrl + Shift + R", binding.DisplayText);
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModShift, binding.Modifiers);
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.R), binding.VirtualKey);
    }

    [Fact]
    public void TryCreateBinding_WithModifierOnlyKey_ReturnsFalse()
    {
        bool created = HotkeyService.TryCreateBinding(Key.LeftCtrl, ModifierKeys.Control, out HotkeyBinding binding);

        Assert.False(created);
        Assert.Equal("F5", binding.DisplayText);
    }

    [Fact]
    public void IsShortcutTrigger_ReturnsTrueWhenRequiredModifiersArePressed()
    {
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Shift);
        HashSet<uint> pressedKeys = [0x11u, 0x10u];
        MacroEvent keyboardEvent = new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, binding.VirtualKey);

        bool triggered = HotkeyService.IsShortcutTrigger(keyboardEvent, binding, pressedKeys);

        Assert.True(triggered);
    }

    [Fact]
    public void IsShortcutTrigger_ReturnsFalseWhenRequiredModifiersAreMissing()
    {
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Shift);
        HashSet<uint> pressedKeys = [0x11u];
        MacroEvent keyboardEvent = new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, binding.VirtualKey);

        bool triggered = HotkeyService.IsShortcutTrigger(keyboardEvent, binding, pressedKeys);

        Assert.False(triggered);
    }

    [Fact]
    public void IsShortcutTrigger_ReturnsFalseWhenExcessModifierIsPressed()
    {
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.F5, ModifierKeys.None);
        HashSet<uint> pressedKeys = [0x11u]; // Ctrl held, but binding has no modifiers
        MacroEvent keyboardEvent = new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.Zero, binding.VirtualKey);

        bool triggered = HotkeyService.IsShortcutTrigger(keyboardEvent, binding, pressedKeys);

        Assert.False(triggered);
    }

    [Fact]
    public void GetShortcutVirtualKeys_IncludesModifierVirtualKeys()
    {
        HotkeyBinding binding = HotkeyService.CreateBinding(Key.R, ModifierKeys.Control | ModifierKeys.Alt);

        HashSet<uint> virtualKeys = HotkeyService.GetShortcutVirtualKeys(binding);

        Assert.Contains(binding.VirtualKey, virtualKeys);
        Assert.Contains(0x11u, virtualKeys);
        Assert.Contains(0xA2u, virtualKeys);
        Assert.Contains(0xA3u, virtualKeys);
        Assert.Contains(0x12u, virtualKeys);
        Assert.Contains(0xA4u, virtualKeys);
        Assert.Contains(0xA5u, virtualKeys);
    }
}
