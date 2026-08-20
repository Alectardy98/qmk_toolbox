// ReSharper disable StringLiteralTypo
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace QMK_Toolbox.Usb.Bootloader;

internal class CaterinaDevice : BootloaderDevice
{
    public CaterinaDevice(KnownHidDevice d) : base(d)
    {
        Type = BootloaderType.Caterina;
        Name = "Caterina";
        IsEepromFlashable = true;
    }

    public string ComPort { get; private set; }

    private string FindComPort()
    {
        foreach (var port in EnumerateCandidatePorts())
        {
            if (PortMatchesUsbDevice(port, VendorId, ProductId))
                return port;
        }

        return null;
    }

    private string FindComPortWithRetry()
    {
        // USB enumeration can occur slightly before cdc_acm creates /dev/ttyACM*.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = FindComPort();
            if (port != null)
                return port;

            Thread.Sleep(100);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePorts()
    {
        return Directory.EnumerateFiles("/dev", "ttyACM*")
            .Concat(Directory.EnumerateFiles("/dev", "ttyUSB*"))
            .OrderBy(x => x, StringComparer.Ordinal);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr realpath(string path, IntPtr resolvedPath);

    [DllImport("libc")]
    private static extern void free(IntPtr ptr);

    private static string RealPath(string path)
    {
        var ptr = realpath(path, IntPtr.Zero);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringAnsi(ptr);
        }
        finally
        {
            free(ptr);
        }
    }

    private static bool PortMatchesUsbDevice(string port, ushort vendorId, ushort productId)
    {
        try
        {
            var ttyName = Path.GetFileName(port);
            var resolved = RealPath($"/sys/class/tty/{ttyName}/device");

            if (resolved == null)
                return false;

            var current = new DirectoryInfo(resolved);

            while (current != null)
            {
                var vendorPath = Path.Combine(current.FullName, "idVendor");
                var productPath = Path.Combine(current.FullName, "idProduct");

                if (File.Exists(vendorPath) && File.Exists(productPath))
                {
                    var vendorText = File.ReadAllText(vendorPath).Trim();
                    var productText = File.ReadAllText(productPath).Trim();

                    if (ushort.TryParse(vendorText, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var foundVendor) &&
                        ushort.TryParse(productText, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var foundProduct) &&
                        foundVendor == vendorId && foundProduct == productId)
                    {
                        return true;
                    }
                }

                current = current.Parent;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Caterina serial discovery error: {ex.Message}");
        }

        return false;
    }

    private static bool CanAccessPort(string port)
    {
        try
        {
            using var stream = new FileStream(port, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public override void Flash(string mcu, string file)
    {
        ComPort = FindComPortWithRetry();

        if (ComPort == null)
        {
            PrintMessage("Serial port not found for Caterina bootloader!", MessageType.Error);
            return;
        }

        if (!CanAccessPort(ComPort))
        {
            PrintMessage($"Permission denied for {ComPort}. Install the QMK udev rules and reconnect the keyboard.", MessageType.Error);
            return;
        }

        PrintMessage($"Using serial port {ComPort}", MessageType.Info);
        RunProcessAsync("avrdude",
            $"-p {mcu} -c avr109 -U flash:w:\"{file}\":i -P {ComPort}").Wait();
    }

    public override void FlashEeprom(string mcu, string file)
    {
        ComPort = FindComPortWithRetry();

        if (ComPort == null)
        {
            PrintMessage("Serial port not found for Caterina bootloader!", MessageType.Error);
            return;
        }

        if (!CanAccessPort(ComPort))
        {
            PrintMessage($"Permission denied for {ComPort}. Install the QMK udev rules and reconnect the keyboard.", MessageType.Error);
            return;
        }

        PrintMessage($"Using serial port {ComPort}", MessageType.Info);
        RunProcessAsync("avrdude",
            $"-p {mcu} -c avr109 -U eeprom:w:\"{file}\":i -P {ComPort}").Wait();
    }

    public override string ToString()
    {
        return base.ToString();
    }
}
