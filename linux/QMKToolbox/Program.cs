using System;
using Avalonia;
using Avalonia.Dialogs;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace QMK_Toolbox
{
    internal class Program
    {
        public static string Arg;
        public static string HexFile; // Add this field to store the hex file path

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            Arg = args.Length == 0 ? string.Empty : args[0];

            // Setup configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            IConfiguration configuration = builder.Build();

            // Access the HexFile configuration value
            HexFile = configuration["Settings:HexFile"];

            // Build and run the Avalonia application
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        private static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseManagedSystemDialogs()
                .LogToTrace()
                .UseReactiveUI();
        }
    }
}
