using System.Runtime.InteropServices;
using System.Windows;

namespace CreteLauncher;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int AttachParent = -1;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 0)
            return; // normal GUI path via StartupUri
        AttachConsole(AttachParent);
        int code;
        try
        {
            code = await Headless.RunAsync(e.Args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("error: " + ex.Message);
            code = 1;
        }
        Console.WriteLine();
        Shutdown(code);
    }
}
