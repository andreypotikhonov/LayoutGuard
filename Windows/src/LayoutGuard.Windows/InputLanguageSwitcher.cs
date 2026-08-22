using LayoutGuard.Core;

namespace LayoutGuard.Windows;

internal static class InputLanguageSwitcher
{
    public static bool Select(SupportedLanguage language)
    {
        var layoutId = language == SupportedLanguage.Russian ? "00000419" : "00000409";
        var layout = NativeMethods.LoadKeyboardLayout(layoutId, NativeMethods.KlfActivate);
        var focused = NativeMethods.GetFocusedInputWindow();
        if (layout == IntPtr.Zero || focused == IntPtr.Zero) return false;

        var thread = NativeMethods.GetWindowThreadProcessId(focused, out _);
        var delivered = NativeMethods.SendMessageTimeout(
            focused,
            NativeMethods.WmInputLangChangeRequest,
            IntPtr.Zero,
            layout,
            NativeMethods.SmtoBlock | NativeMethods.SmtoAbortIfHung,
            100,
            out _);
        if (delivered == IntPtr.Zero) return false;

        var selected = NativeMethods.GetKeyboardLayout(thread);
        var expectedLanguageId = language == SupportedLanguage.Russian ? 0x0419 : 0x0409;
        return ((long)selected & 0xffff) == expectedLanguageId;
    }
}
