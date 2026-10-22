using System;
using Avalonia;
using Avalonia.ReactiveUI;

namespace QMK_Toolbox;

internal class Program
{
    public static string Arg;

    [STAThread]
    public static void Main(string[] args)
    {
        Arg = args.Length == 0 ? string.Empty : args[0];
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new X11PlatformOptions { WmClass = "QMKToolbox" })
        .LogToTrace().UseReactiveUI();
}
