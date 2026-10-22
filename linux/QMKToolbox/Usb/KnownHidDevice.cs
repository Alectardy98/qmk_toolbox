using QMK_Toolbox.Usb.Bootloader;

namespace QMK_Toolbox.Usb;

internal class KnownHidDevice
{
    public string Identity { get; init; }
    public string SerialNumber { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }

    public ushort RevisionBcd { get; set; } = 0x0100;
    public string VendorName { get; init; }
    public string ProductName { get; init; }
    public BootloaderType BootloaderType { get; init; }
    public override string ToString() => $"{VendorName} {ProductName} ({VendorId:x4}:{ProductId:x4}) [{Identity}] serial={SerialNumber ?? "unavailable"}";
}