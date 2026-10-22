using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using QMK_Toolbox.ViewModels;
using QMK_Toolbox.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QMK_Toolbox.Helpers;
using QMK_Toolbox.Usb;
using QMK_Toolbox.Usb.Bootloader;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
        Console.WriteLine("PASS " + name);
    }

    public static async Task Main()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux tests");
        Check(HidConsole.IsConsoleDescriptor(new byte[] { 0x06, 0x31, 0xff, 0x09, 0x74, 0xa1, 0x01 }), "PJRC console descriptor");
        Check(!HidConsole.IsConsoleDescriptor(new byte[] { 0x05, 0x01, 0x09, 0x06, 0xa1, 0x01 }), "Keyboard excluded");
        Check(!HidConsole.IsConsoleDescriptor(new byte[] { 0x06, 0x31 }), "Truncated descriptor rejected");
        Check(HidConsole.IsConsoleDescriptor(new byte[] { 0x0b, 0x74, 0, 0x31, 0xff, 0xa1, 0x01 }), "Extended usage");
        Check(HidConsole.IsConsoleDescriptor(new byte[] { 0x06, 0x31, 0xff, 0xa4, 0x05, 1, 0xb4, 0x09, 0x74, 0xa1, 1 }), "Global push/pop");
        Check(!HidConsole.IsConsoleDescriptor(new byte[] { 0xfe, 10, 0 }), "Truncated long item rejected");
        var comparer = new UsbDeviceRecordComparer();
        var first = new UsbDeviceNameRecord { Identity = "1-1", VendorId = 1, ProductId = 2 };
        var second = new UsbDeviceNameRecord { Identity = "1-2", VendorId = 1, ProductId = 2 };
        Check(!comparer.Equals(first, second), "Identical models on separate ports remain distinct");
        Check(comparer.Equals(first, new UsbDeviceNameRecord { Identity = "1-1", VendorId = 1, ProductId = 2 }), "Stable device identity");
        Check(LufaMsDevice.DecodeMountPath("/media/my\\040keyboard\\134x") == "/media/my keyboard\\x", "Escaped mount paths");
        var listener = new UsbListener();
        var expected = new (ushort Vendor, ushort Product, BootloaderType Type)[]
        {
            (0x03eb, 0x2045, BootloaderType.LufaMs), (0x03eb, 0x6124, BootloaderType.AtmelSamBa),
            (0x0483, 0xdf11, BootloaderType.Stm32Dfu), (0x1209, 0x2302, BootloaderType.Caterina),
            (0x16c0, 0x0478, BootloaderType.HalfKay), (0x16c0, 0x0483, BootloaderType.AvrIsp),
            (0x16c0, 0x05dc, BootloaderType.UsbAsp), (0x16c0, 0x05df, BootloaderType.BootloadHid),
            (0x1781, 0x0c9f, BootloaderType.UsbTinyIsp), (0x1c11, 0xb007, BootloaderType.KiibohdDfu),
            (0x1eaf, 0x0003, BootloaderType.Stm32Duino), (0x1ffb, 0x0101, BootloaderType.Caterina),
            (0x239a, 0x000c, BootloaderType.Caterina), (0x28e9, 0x0189, BootloaderType.Gd32VDfu),
            (0x314b, 0x0106, BootloaderType.Apm32Dfu), (0x342d, 0xdfa0, BootloaderType.Wb32Dfu),
            (0x03eb, 0x2067, BootloaderType.LufaHid), (0x03eb, 0x2ff4, BootloaderType.AtmelDfu)
        };
        foreach (var entry in expected)
        {
            var found = listener.ResolveDevice(new UsbDeviceNameRecord { VendorId = entry.Vendor,
                ProductId = entry.Product, DeviceRevisionBcd = 0x0100, Identity = "test-port", HasDfuInterface = true });
            Check(found?.Type == entry.Type && found.Identity == "test-port", $"Upstream ID {entry.Vendor:x4}:{entry.Product:x4} -> {entry.Type}");
        }
        Check(listener.ResolveDevice(new UsbDeviceNameRecord { VendorId = 0x03eb, ProductId = 0x2ff4,
            DeviceRevisionBcd = 0x0936 })?.Type == BootloaderType.QmkDfu, "QMK DFU revision");
        Check(listener.ResolveDevice(new UsbDeviceNameRecord { VendorId = 0x03eb, ProductId = 0x2067,
            DeviceRevisionBcd = 0x0936 })?.Type == BootloaderType.QmkHid, "QMK HID revision");
        Check(listener.ResolveDevice(new UsbDeviceNameRecord { VendorId = 0xffff, ProductId = 0xffff }) == null, "Unknown USB devices ignored");
        EmbeddedResourceHelper.ExtractResources("reset.eep", "avrdude.conf");
        Check(File.ReadAllBytes(Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "reset.eep")).Length > 0, "EEPROM image extracted");
        Check((File.GetUnixFileMode(EmbeddedResourceHelper.ResourceDirectory) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite | UnixFileMode.OtherRead)) == 0, "Private tool directory");
        var tool = Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "fake-tool");
        File.WriteAllText(tool, "#!/bin/sh\nprintf 'test output\\n'\nexit 0\n");
        LinuxPermissions.MakeExecutable(tool);
        var device = new TestDevice();
        Check(await device.Run("fake-tool") == 0, "Process completion");
        File.WriteAllText(tool, "#!/bin/sh\nexit 17\n");
        var failed = false;
        try { await device.Run("fake-tool"); } catch (IOException ex) { failed = ex.Message.Contains("17"); }
        Check(failed, "Nonzero exit propagates failure");
        var absent = false;
        try { await device.Run("missing-tool"); } catch (FileNotFoundException) { absent = true; }
        Check(absent, "Missing tool reported");
        File.WriteAllText(tool, "#!/bin/sh\nprintf '%s' \"$1\" > received\n");
        var unusualPath = "firm ware\\odd\"name.hex";
        await device.Run("fake-tool", TestDevice.QuoteArgument(unusualPath));
        Check(File.ReadAllText(Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "received")) == unusualPath,
            "Firmware argument preserves spaces, backslashes and quotes");
        var programmer = Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "dfu-programmer");
        File.WriteAllText(programmer, "#!/bin/sh\nprintf '%s\\n' \"$*\" >> sequence\ncase \"$*\" in *flash*) exit 9;; esac\n");
        LinuxPermissions.MakeExecutable(programmer);
        try { new AtmelDfuDevice(new KnownHidDevice()).Flash("atmega32u4", "firmware.hex"); }
        catch (AggregateException) { }
        var sequence = File.ReadAllLines(Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "sequence"));
        Check(sequence.Length == 2 && sequence[0].Contains("erase") && sequence[1].Contains("flash"),
            "Failed DFU flash prevents subsequent reset");
        EmbeddedResourceHelper.Cleanup();
        AppBuilder.Configure<QMK_Toolbox.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        var view = new MainWindow();
        var vm = new MainWindowViewModel("", view);
        ((QMK_Toolbox.App)Avalonia.Application.Current).MainWindowViewModel = vm;
        view.DataContext = vm;
        view.Measure(new Size(720, 440));
        view.Arrange(new Rect(0, 0, 720, 440));
        Dispatcher.UIThread.RunJobs();
        Check(!vm.CanFlash && !vm.CanClearEeprom && !vm.CanReset, "Disconnected UI disables device operations");
        Check(vm.CanOpenFile && !vm.IsAutoFlash, "File picker enabled and auto flash opt-in");
        Check(view.Width == 960 && view.MinWidth == 720, "Window uses logical desktop dimensions");
        Check(view.FindControl<TextBlock>("HexFile") != null, "Firmware file binding loads in headless view");
        Stm32Tests.Run(Check);
        EmbeddedResourceHelper.Cleanup();
        Console.WriteLine($"{_checks} checks passed; no USB writes performed.");
    }
    private sealed class TestDevice : BootloaderDevice
    {
        internal TestDevice() : base(new KnownHidDevice()) { }
        internal Task<int> Run(string tool, string args = "") => RunProcessAsync(tool, args);
        internal static string QuoteArgument(string value) => Quote(value);
    }
}
