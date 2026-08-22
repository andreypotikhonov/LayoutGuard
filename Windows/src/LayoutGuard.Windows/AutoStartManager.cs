using Microsoft.Win32;

namespace LayoutGuard.Windows;

internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LayoutGuard";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --background");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

