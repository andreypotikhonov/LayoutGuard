using System.Threading;

namespace LayoutGuard.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\LayoutGuard.Windows.Singleton", out var firstInstance);
        if (!firstInstance) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

