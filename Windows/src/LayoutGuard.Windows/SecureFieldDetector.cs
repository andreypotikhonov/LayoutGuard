using System.Diagnostics;
using UIA = Interop.UIAutomationClient;

namespace LayoutGuard.Windows;

internal sealed class SecureFieldDetector
{
    private const int IsPasswordPropertyId = 30019;
    private readonly UIA.IUIAutomation _automation = new UIA.CUIAutomation8Class();

    public bool ShouldPause(AppSettings settings)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.Id == Environment.ProcessId ||
                settings.ExcludedProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }

        try
        {
            var focused = _automation.GetFocusedElement();
            return focused?.GetCurrentPropertyValue(IsPasswordPropertyId) is true;
        }
        catch
        {
            return false;
        }
    }
}
