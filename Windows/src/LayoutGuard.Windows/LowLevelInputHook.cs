using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LayoutGuard.Windows;

internal sealed record KeyStroke(uint VirtualKey, uint ScanCode, string Text, bool Injected);

internal sealed class LowLevelInputHook : IDisposable
{
    private readonly Func<KeyStroke, bool> _keyHandler;
    private readonly Action _mouseHandler;
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    public LowLevelInputHook(Func<KeyStroke, bool> keyHandler, Action mouseHandler)
    {
        _keyHandler = keyHandler;
        _mouseHandler = mouseHandler;
        _keyboardCallback = KeyboardCallback;
        _mouseCallback = MouseCallback;
    }

    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl, _keyboardCallback, moduleHandle, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl, _mouseCallback, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось включить глобальный перехват клавиатуры.");
        }
    }

    private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr dataPointer)
    {
        if (code >= 0 && (message == (IntPtr)NativeMethods.WmKeyDown ||
                          message == (IntPtr)NativeMethods.WmSysKeyDown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(dataPointer);
            var injected = (data.Flags & NativeMethods.LlkhfInjected) != 0 ||
                data.ExtraInfo == NativeMethods.InjectionSignature;
            var stroke = new KeyStroke(data.VirtualKey, data.ScanCode, Translate(data), injected);
            if (_keyHandler(stroke)) return (IntPtr)1;
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, dataPointer);
    }

    private IntPtr MouseCallback(int code, IntPtr message, IntPtr dataPointer)
    {
        if (code >= 0 && (message == (IntPtr)NativeMethods.WmLButtonDown ||
                          message == (IntPtr)NativeMethods.WmRButtonDown ||
                          message == (IntPtr)NativeMethods.WmMButtonDown))
        {
            _mouseHandler();
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, message, dataPointer);
    }

    private static string Translate(NativeMethods.KeyboardHookData data)
    {
        var state = new byte[256];
        if (!NativeMethods.GetKeyboardState(state)) return string.Empty;
        var focused = NativeMethods.GetFocusedInputWindow();
        var thread = NativeMethods.GetWindowThreadProcessId(focused, out _);
        var layout = NativeMethods.GetKeyboardLayout(thread);
        var buffer = new StringBuilder(8);
        var count = NativeMethods.ToUnicodeEx(
            data.VirtualKey, data.ScanCode, state, buffer, buffer.Capacity, 0, layout);
        return count > 0 ? buffer.ToString(0, count) : string.Empty;
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }
}
