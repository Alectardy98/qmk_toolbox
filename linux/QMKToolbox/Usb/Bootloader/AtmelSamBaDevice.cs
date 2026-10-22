using System.Threading.Tasks;

// ReSharper disable StringLiteralTypo
namespace QMK_Toolbox.Usb.Bootloader;

internal class AtmelSamBaDevice : BootloaderDevice
{
    public AtmelSamBaDevice(KnownHidDevice d) : base(d)
    {
        Type = BootloaderType.AtmelSamBa;
        Name = "Atmel SAM-BA";
        IsResettable = true;

    }

    private string ComPort => CaterinaDevice.FindComPort(VendorId, ProductId, Identity);

    public override void Flash(string mcu, string file)
    {
        if (ComPort == null)
        {
            PrintMessage("COM port not found!", MessageType.Error);
            return;
        }

        RunProcessAsync("mdloader", $"-p {ComPort} -D {Quote(file)} --restart").Wait();
    }

    public override void Reset(string mcu)
    {
        if (ComPort == null)
        {
            PrintMessage("COM port not found!", MessageType.Error);
            return;
        }

        RunProcessAsync("mdloader", $"-p {ComPort} --restart").Wait();
    }

    public override string ToString()
    {
        return $"{base.ToString()} [{ComPort}]";
    }
}