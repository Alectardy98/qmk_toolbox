using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace QMK_Toolbox.Helpers
{
    internal static class EmbeddedResourceHelper
    {
        public static readonly string[] Resources =
        {
            "avrdude.conf",
            "reset.eep",
            "reset_left.eep",
            "reset_right.eep",
            "avrdude",
            "bootloadHID",
            "dfu-programmer",
            "dfu-util",
            "hid_bootloader_cli",
            "mdloader",
            "teensy_loader_cli",
            "wb32-dfu-updater_cli",
            "libftdi1.so",
            "libusb.so",
            "libhidapi-libusb.so",
            "libusb-1.0.dll",
            "wb32-dfu-updater_cli",
        };

        // A private directory prevents stale or other users' /tmp tools being executed.
        internal static readonly string ResourceDirectory = CreateResourceDirectory();

        private static string CreateResourceDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "qmk-toolbox-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            LinuxPermissions.SetPrivateDirectory(path);
            return path;
        }

        internal static void ExtractResource(string file)
        {
            var destPath = Path.Combine(ResourceDirectory, file);
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"QMK_Toolbox.Resources.{file}")
                ?? throw new FileNotFoundException($"Missing bundled resource: {file}");
            using (var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                stream.CopyTo(output);
            LinuxPermissions.MakeExecutable(destPath);
        }

        internal static void Cleanup()
        {
            if (Directory.Exists(ResourceDirectory)) Directory.Delete(ResourceDirectory, true);
        }

        internal static void ExtractResources(params string[] files) => ExtractResources(files as IEnumerable<string>);

        private static void ExtractResources(IEnumerable<string> files)
        {
            foreach (var s in files)
            {
                ExtractResource(s);
            }
        }

        internal static string GetResourceContent(string file)
        {
            using var stream = 
                Assembly.GetExecutingAssembly().GetManifestResourceStream($"QMK_Toolbox.Resources.{file}");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
