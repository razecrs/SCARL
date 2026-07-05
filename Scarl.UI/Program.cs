using Avalonia;
using System;

namespace Scarl.UI;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppBuilder.Configure<App>()
    // has been run.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            App.RunCliDirect(args);
        }
        else
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    // AppBuilder configures or parameterizes Avalonia framework.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
