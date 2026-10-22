using QMK_Toolbox.Stm32;

namespace QMK_Toolbox.Usb.Bootloader;

internal class Stm32DfuDevice : Stm32Device
{
    public Stm32DfuDevice(KnownHidDevice d) : base(d)
    {
        Type = BootloaderType.Stm32Dfu;
        Name = "STM32 DfuSe USB (ROM-compatible; confirm MCU)";
        IsResettable = true;
    }
    internal override Stm32Protocol Protocol => Stm32Protocol.RomDfu;
    protected override void Preflight(Stm32Target target)
    {
        RunProcessAsync("dfu-util", $"{UsbSelector()} -l").GetAwaiter().GetResult();
        Stm32DfuLayout.Validate(LastProcessOutput, Identity, target, Protocol);
    }

    internal override string BuildArguments(Stm32Firmware image, string snapshot) =>
        $"{UsbSelector()} -a 0 -s 0x{image.Address:x8}:leave -D {Quote(snapshot)}";

    public override void Reset(string target)
    {
        var address = Stm32Target.Find(target).ApplicationAddress(Protocol);
        RunProcessAsync("dfu-util", $"{UsbSelector()} -a 0 -s 0x{address:x8}:leave").GetAwaiter().GetResult();
    }
}
