using System.Diagnostics;
using UIA = Interop.UIAutomationClient;

namespace LayoutGuard.Windows;

internal sealed class SecureFieldDetector
{
    private const int IsPasswordPropertyId = 30019;
    private readonly UIA.IUIAutomation _automation = new UIA.CUIAutomation8Class();
    private long _lastCheck;
    private bool _lastResult;

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

        var now = Environment.TickCount64;
        if (now - _lastCheck < 350) return _lastResult;
        _lastCheck = now;
        try
        {
            var focused = _automation.GetFocusedElement();
            _lastResult = focused?.GetCurrentPropertyValue(IsPasswordPropertyId) is true;
        }
        catch
        {
            _lastResult = false;
        }
        return _lastResult;
    }
}
