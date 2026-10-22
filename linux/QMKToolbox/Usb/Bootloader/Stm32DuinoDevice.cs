using QMK_Toolbox.Stm32;

namespace QMK_Toolbox.Usb.Bootloader;

internal class Stm32DuinoDevice : Stm32Device
{
    public Stm32DuinoDevice(KnownHidDevice d) : base(d)
    {
        Type = BootloaderType.Stm32Duino;
        Name = "STM32duino USB (F103, 8 KB bootloader / alt 2)";
    }
    internal override Stm32Protocol Protocol => Stm32Protocol.Stm32Duino;
    protected override void Preflight(Stm32Target target)
    {
        RunProcessAsync("dfu-util", $"{UsbSelector()} -l").GetAwaiter().GetResult();
        Stm32DfuLayout.Validate(LastProcessOutput, Identity, target, Protocol);
    }

    internal override string BuildArguments(Stm32Firmware image, string snapshot) =>
        $"{UsbSelector()} -a 2 -R -D {Quote(snapshot)}";
}
