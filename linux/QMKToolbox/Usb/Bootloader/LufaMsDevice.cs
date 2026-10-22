using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace QMK_Toolbox.Usb.Bootloader;

internal class LufaMsDevice : BootloaderDevice
{
    public LufaMsDevice(KnownHidDevice d) : base(d)
    {
        Type = BootloaderType.LufaMs;
        Name = "LUFA MS";
    }

    public override void Flash(string mcu, string file)
    {
        if (!string.Equals(Path.GetExtension(file), ".bin", StringComparison.OrdinalIgnoreCase))
            throw new IOException("LUFA mass storage requires a .bin file.");
        var mounts = File.ReadLines("/proc/self/mountinfo").Select(line => line.Split(' '))
            .Where(fields => fields.Length > 5 && MatchesDevice(fields[2]))
            .Select(fields => DecodeMountPath(fields[4])).Distinct().ToArray();
        if (mounts.Length != 1) throw new IOException("Cannot identify a unique mounted LUFA bootloader volume.");
        var destination = Path.Combine(mounts[0], "FLASH.BIN");
        PrintMessage($"Writing {destination}", MessageType.Command);
        using (var source = File.OpenRead(file))
        using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(target);
            target.Flush(true);
        }
        PrintMessage("Firmware copied. Eject the drive to finish.", MessageType.Bootloader);
    }

    internal static string DecodeMountPath(string path) => path.Replace("\\040", " ")
        .Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");

    private bool MatchesDevice(string number)
    {
        var link = new DirectoryInfo("/sys/dev/block/" + number);
        if (!link.Exists) return false;
        var current = link.ResolveLinkTarget(true) as DirectoryInfo;
        while (current != null)
        {
            var vendor = Path.Combine(current.FullName, "idVendor");
            var product = Path.Combine(current.FullName, "idProduct");
            if (File.Exists(vendor) && File.Exists(product))
                return ushort.Parse(File.ReadAllText(vendor).Trim(), NumberStyles.HexNumber) == VendorId &&
                    ushort.Parse(File.ReadAllText(product).Trim(), NumberStyles.HexNumber) == ProductId && current.Name == Identity;
            current = current.Parent;
        }
        return false;
    }
}
