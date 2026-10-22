using System;
using System.IO;
using QMK_Toolbox.Helpers;
using QMK_Toolbox.Stm32;

namespace QMK_Toolbox.Usb.Bootloader;

internal abstract class Stm32Device : BootloaderDevice
{
    protected Stm32Device(KnownHidDevice device) : base(device) { }
    internal abstract Stm32Protocol Protocol { get; }
    public override bool RequiresStm32Target => true;
    public override void ValidateFirmware(string target, string file) =>
        Stm32Firmware.Load(file, Stm32Target.Find(target), Protocol);

    public override void Flash(string target, string file)
    {
        ValidateFirmware(target, file);
        var profile = Stm32Target.Find(target);
        var image = Stm32Firmware.Load(file, profile, Protocol);
        var snapshot = Path.Combine(EmbeddedResourceHelper.ResourceDirectory, "stm32-" + Guid.NewGuid().ToString("N") + ".bin");
        Directory.CreateDirectory(EmbeddedResourceHelper.ResourceDirectory);
        LinuxPermissions.SetPrivateDirectory(EmbeddedResourceHelper.ResourceDirectory);
        try
        {
            Preflight(profile);
            File.WriteAllBytes(snapshot, image.Data);
            PrintMessage($"{Name}: {profile.Name}, {image.Data.Length} bytes at 0x{image.Address:x8}. MCU choice is user-confirmed; USB IDs do not prove chip identity.", MessageType.Info);
            RunProcessAsync(Tool, BuildArguments(image, snapshot)).GetAwaiter().GetResult();
            PrintMessage("STM32 transfer completed. Check the firmware starts; a reset or BOOT0 change may be needed.", MessageType.Info);
        }
        finally { File.Delete(snapshot); }
    }

    protected virtual void Preflight(Stm32Target target) { }

    protected virtual string Tool => "dfu-util";
    internal abstract string BuildArguments(Stm32Firmware image, string snapshot);

    protected string UsbSelector()
    {
        // Match DFU mode only and the physical sysfs/libusb bus-port path.
        if (string.IsNullOrEmpty(Identity) || !System.Text.RegularExpressions.Regex.IsMatch(Identity, @"^\d+-\d+(\.\d+)*$"))
            throw new IOException("STM32 USB port identity is unavailable; reconnect the target.");
        return $"-d ,{VendorId:x4}:{ProductId:x4} -p {Quote(Identity)}";
    }
}
