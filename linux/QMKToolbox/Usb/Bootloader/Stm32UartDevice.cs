using System.IO;
using System.Text.RegularExpressions;
using QMK_Toolbox.Stm32;

namespace QMK_Toolbox.Usb.Bootloader;

// Never synthesized from a USB-UART adapter ID or used by Auto-Flash.
internal class Stm32UartDevice : Stm32Device
{
    private readonly string _port;
    public Stm32UartDevice(string port) : base(new KnownHidDevice
        { Identity = port, VendorName = "User-selected UART", ProductName = "STM32 ROM bootloader" })
    {
        _port = port;
        Name = "STM32 ROM UART (manual)";
        Type = BootloaderType.Stm32Uart;
    }
    internal override Stm32Protocol Protocol => Stm32Protocol.RomUart;
    protected override string Tool => "stm32flash";
    public override void ValidateFirmware(string target, string file)
    {
        base.ValidateFirmware(target, file);
        if (string.IsNullOrEmpty(_port) || !Regex.IsMatch(_port, @"^/dev/(tty(USB|ACM|S)\d+|serial/by-id/[^/]+)$") || !File.Exists(_port))
            throw new IOException("Choose an existing /dev/ttyUSB*, ttyACM*, ttyS*, or /dev/serial/by-id/ port. UART flashing is manual and requires a correctly wired ROM-boot-mode target.");
    }
    public override void Flash(string target, string file)
    {
        ValidateFirmware(target, file);
        var profile = Stm32Target.Find(target);
        RunProcessAsync(Tool, $"-b 115200 -m 8e1 {Quote(_port)}").GetAwaiter().GetResult();
        var match = Regex.Match(LastProcessOutput, @"Device ID\s*:\s*0x([0-9a-fA-F]+)");
        var expected = profile.IsF103 ? 0x410 : profile.Name.StartsWith("STM32F401") ? 0x423 : 0x431;
        if (!match.Success || System.Convert.ToInt32(match.Groups[1].Value, 16) != expected)
            throw new IOException("UART chip identification does not match the selected MCU family; no firmware write was started.");
        base.Flash(target, file);
    }

    internal override string BuildArguments(Stm32Firmware image, string snapshot)
    {
        // Validation also runs in the shared Flash method, before invoking a tool.
        if (string.IsNullOrEmpty(_port) || !Regex.IsMatch(_port, @"^/dev/(tty(USB|ACM|S)\d+|serial/by-id/[^/]+)$"))
            throw new IOException("Invalid UART device path.");
        return $"-c -b 115200 -m 8e1 -f -S 0x{image.Address:x8}:{image.Data.Length} -w {Quote(snapshot)} -v -g 0x0 {Quote(_port)}";
    }
}
