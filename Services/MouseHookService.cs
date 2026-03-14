using System.Diagnostics;
using System.Runtime.InteropServices;
using Sreapeat.Helpers;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class MouseHookService : IDisposable
{
    private readonly NativeMethods.HookProc _hookCallback;
    private nint _hookHandle;
    private bool _disposed;

    public MouseHookService()
    {
        _hookCallback = HookCallback;
    }

    public event EventHandler<MacroEvent>? MouseActionCaptured;

    public Func<int, int, bool>? ShouldIgnorePoint { get; set; }

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
            NativeMethods.WhMouseLl,
            _hookCallback,
            moduleHandle,
            0);

        if (_hookHandle == nint.Zero)
        {
            throw new InvalidOperationException("Unable to install the global mouse hook.");
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
            throw new InvalidOperationException("Unable to remove the global mouse hook.");
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
            NativeMethods.MsLlHookStruct hookStruct =
                Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);

            if (ShouldIgnorePoint?.Invoke(hookStruct.Pt.X, hookStruct.Pt.Y) == true)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            MacroEvent? capturedEvent = TryCreateEvent((int)wParam, hookStruct);
            if (capturedEvent is not null)
            {
                MouseActionCaptured?.Invoke(this, capturedEvent);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static MacroEvent? TryCreateEvent(int message, NativeMethods.MsLlHookStruct hookStruct)
    {
        MacroEventType? eventType = message switch
        {
            NativeMethods.WmMouseMove => MacroEventType.Move,
            NativeMethods.WmLButtonDown => MacroEventType.LeftDown,
            NativeMethods.WmLButtonUp => MacroEventType.LeftUp,
            NativeMethods.WmRButtonDown => MacroEventType.RightDown,
            NativeMethods.WmRButtonUp => MacroEventType.RightUp,
            NativeMethods.WmMButtonDown => MacroEventType.MiddleDown,
            NativeMethods.WmMButtonUp => MacroEventType.MiddleUp,
            NativeMethods.WmMouseWheel => MacroEventType.Wheel,
            _ => null,
        };

        if (eventType is null)
        {
            return null;
        }

        short delta = eventType == MacroEventType.Wheel
            ? unchecked((short)((hookStruct.MouseData >> 16) & 0xFFFF))
            : (short)0;

        return new MacroEvent(
            eventType.Value,
            hookStruct.Pt.X,
            hookStruct.Pt.Y,
            delta,
            TimeSpan.Zero);
    }
}
