using LayoutGuard.Core;

namespace LayoutGuard.Windows;

internal static class InputLanguageSwitcher
{
    public static bool Select(SupportedLanguage language)
    {
        var layoutId = language == SupportedLanguage.Russian ? "00000419" : "00000409";
        var layout = NativeMethods.LoadKeyboardLayout(layoutId, NativeMethods.KlfActivate);
        var foreground = NativeMethods.GetForegroundWindow();
        return layout != IntPtr.Zero && foreground != IntPtr.Zero &&
            NativeMethods.PostMessage(foreground, NativeMethods.WmInputLangChangeRequest, IntPtr.Zero, layout);
    }
}

