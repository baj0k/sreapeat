using System.Diagnostics;
using System.Runtime.InteropServices;
using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class KeyboardHookService : IHookService, IDisposable
{
    private readonly NativeMethods.HookProc _hookCallback;
    private nint _hookHandle;
    private bool _disposed;

    public KeyboardHookService()
    {
        _hookCallback = HookCallback;
    }

    public event EventHandler<MacroEvent>? KeyboardActionCaptured;

    public bool IsRunning => _hookHandle != nint.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        using Process currentProcess = Process.GetCurrentProcess();
        using ProcessModule? module = currentProcess.MainModule;
        nint moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookCallback,
            moduleHandle,
            0);

        if (_hookHandle == nint.Zero)
        {
            throw new InvalidOperationException("Unable to install the global keyboard hook.");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
        {
            throw new InvalidOperationException("Unable to remove the global keyboard hook.");
        }

        _hookHandle = nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsRunning)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            NativeMethods.KbdLlHookStruct hookStruct =
                Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);

            MacroEvent? capturedEvent = TryCreateEvent((int)wParam, hookStruct);
            if (capturedEvent is not null)
            {
                KeyboardActionCaptured?.Invoke(this, capturedEvent);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static MacroEvent? TryCreateEvent(int message, NativeMethods.KbdLlHookStruct hookStruct)
    {
        MacroEventType? eventType = message switch
        {
            NativeMethods.WmKeyDown => MacroEventType.KeyDown,
            NativeMethods.WmSysKeyDown => MacroEventType.KeyDown,
            NativeMethods.WmKeyUp => MacroEventType.KeyUp,
            NativeMethods.WmSysKeyUp => MacroEventType.KeyUp,
            _ => null,
        };

        if (eventType is null)
        {
            return null;
        }

        return new MacroEvent(
            eventType.Value,
            0,
            0,
            0,
            TimeSpan.Zero,
            hookStruct.VkCode,
            (ushort)hookStruct.ScanCode,
            (hookStruct.Flags & NativeMethods.LlkhfExtended) != 0,
            (hookStruct.Flags & NativeMethods.LlkhfInjected) != 0);
    }
}
