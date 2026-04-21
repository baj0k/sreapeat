using System.Collections.Generic;
using System.Windows.Input;
using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal static class HotkeyService
{
    public static bool TryCreateBinding(Key key, ModifierKeys modifiers, out HotkeyBinding binding)
    {
        if (IsModifierKey(key))
        {
            binding = CreateBinding(Key.F5, ModifierKeys.None);
            return false;
        }

        binding = CreateBinding(key, modifiers);
        return true;
    }

    public static HotkeyBinding CreateBinding(Key key, ModifierKeys modifiers)
    {
        return new HotkeyBinding(
            FormatHotkeyText(modifiers, key),
            ConvertModifiers(modifiers),
            (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    public static bool IsShortcutTrigger(MacroEvent keyboardEvent, HotkeyBinding binding, IReadOnlySet<uint> pressedPhysicalKeys)
    {
        if (keyboardEvent.Type != MacroEventType.KeyDown || keyboardEvent.VirtualKey != binding.VirtualKey)
        {
            return false;
        }

        bool ctrlRequired = (binding.Modifiers & NativeMethods.ModControl) != 0;
        bool ctrlPressed = IsAnyPressed(pressedPhysicalKeys, 0x11, 0xA2, 0xA3);
        if (ctrlRequired != ctrlPressed) return false;

        bool shiftRequired = (binding.Modifiers & NativeMethods.ModShift) != 0;
        bool shiftPressed = IsAnyPressed(pressedPhysicalKeys, 0x10, 0xA0, 0xA1);
        if (shiftRequired != shiftPressed) return false;

        bool altRequired = (binding.Modifiers & NativeMethods.ModAlt) != 0;
        bool altPressed = IsAnyPressed(pressedPhysicalKeys, 0x12, 0xA4, 0xA5);
        if (altRequired != altPressed) return false;

        bool winRequired = (binding.Modifiers & NativeMethods.ModWin) != 0;
        bool winPressed = IsAnyPressed(pressedPhysicalKeys, 0x5B, 0x5C);
        if (winRequired != winPressed) return false;

        return true;
    }

    public static HashSet<uint> GetShortcutVirtualKeys(HotkeyBinding binding)
    {
        HashSet<uint> virtualKeys = [binding.VirtualKey];

        if ((binding.Modifiers & NativeMethods.ModControl) != 0)
        {
            virtualKeys.UnionWith([0x11u, 0xA2u, 0xA3u]);
        }

        if ((binding.Modifiers & NativeMethods.ModShift) != 0)
        {
            virtualKeys.UnionWith([0x10u, 0xA0u, 0xA1u]);
        }

        if ((binding.Modifiers & NativeMethods.ModAlt) != 0)
        {
            virtualKeys.UnionWith([0x12u, 0xA4u, 0xA5u]);
        }

        if ((binding.Modifiers & NativeMethods.ModWin) != 0)
        {
            virtualKeys.UnionWith([0x5Bu, 0x5Cu]);
        }

        return virtualKeys;
    }

    private static bool IsAnyPressed(IReadOnlySet<uint> pressedPhysicalKeys, params ReadOnlySpan<uint> virtualKeys)
    {
        foreach (uint virtualKey in virtualKeys)
        {
            if (pressedPhysicalKeys.Contains(virtualKey))
            {
                return true;
            }
        }

        return false;
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
}
