using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using QMK_Toolbox;
using QMK_Toolbox.Stm32;
using QMK_Toolbox.Usb;
using QMK_Toolbox.Usb.Bootloader;
using QMK_Toolbox.ViewModels;
using QMK_Toolbox.Views;

internal static class Stm32Tests
{
    private static Action<bool, string> Check;
    private static string Root;
    private static string Write(string name, byte[] data) { var file = Path.Combine(Root, name); File.WriteAllBytes(file, data); return file; }
    private static byte[] Image(uint address, int length = 256, uint stack = 0x20004000)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, stack);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), address + 0x21);
        return bytes;
    }
    private static void Reject(Action action, string description)
    {
        try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { Check(true, description); return; }
        Check(false, description);
    }
    private static UsbDeviceNameRecord Record(ushort vendor = 0x0483, ushort product = 0xdf11, bool dfu = true) =>
        new() { VendorId = vendor, ProductId = product, Identity = "1-2.3", HasDfuInterface = dfu, ManufacturerName = "ST", ProductName = "STM32 BOOTLOADER", SerialNumber = "fixture" };
    private static KnownHidDevice RomDevice() => new() { VendorId = 0x0483, ProductId = 0xdf11, Identity = "1-2.3" };
    private static string HexRecord(ushort address, byte type, byte[] data)
    {
        var record = new List<byte> { (byte)data.Length, (byte)(address >> 8), (byte)address, type };
        record.AddRange(data); record.Add(unchecked((byte)-record.Sum(x => (int)x)));
        return ":" + Convert.ToHexString(record.ToArray());
    }
    private static byte[] Dfu(byte[] image, uint address = 0x08000000)
    {
        var result = new byte[309 + image.Length];
        Encoding.ASCII.GetBytes("DfuSe").CopyTo(result, 0); result[5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(6), (uint)result.Length - 16); result[10] = 1;
        Encoding.ASCII.GetBytes("Target").CopyTo(result, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(277), (uint)image.Length + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(281), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(285), address);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(289), (uint)image.Length); image.CopyTo(result, 293);
        var suffix = result.Length - 16;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(suffix), 0xffff);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(suffix + 2), 0xdf11);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(suffix + 4), 0x0483);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(suffix + 6), 0x011a);
        Encoding.ASCII.GetBytes("UFD").CopyTo(result, suffix + 8); result[suffix + 11] = 16;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(result.Length - 4), Stm32Firmware.Crc(result.AsSpan(0, result.Length - 4)));
        return result;
    }
    private static void Connect(UsbListener listener, BootloaderDevice device)
    {
        device.OutputReceived = (sender, data, type) => listener.OutputReceived?.Invoke(sender, data, type);
        listener.BootloaderDeviceConnected?.Invoke(device);
    }

    private static void Drain(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do { Dispatcher.UIThread.RunJobs(); if (complete()) return; Thread.Sleep(2); } while (DateTime.UtcNow < deadline);
        throw new Exception("Simulated operation did not complete");
    }

    internal static void Run(Action<bool, string> check)
    {
        Check = check;
        Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../build/stm32/test-fixtures"));
        Directory.CreateDirectory(Root);
        var f103 = Stm32Target.All[0]; var f103cb = Stm32Target.All[1];
        var f401 = Stm32Target.All[2]; var f411 = Stm32Target.All[3];
        var firmware = Write("f401.bin", Image(0x08000000));
        var duinoFirmware = Write("f103-duino.bin", Image(0x08002000));
        var listener = new UsbListener();
        Check(listener.ResolveDevice(Record()) is Stm32DfuDevice, "STM32 ROM DFU USB ID and DFU interface select ROM backend");
        Check(listener.ResolveDevice(Record(0x1eaf, 3)) is Stm32DuinoDevice, "STM32duino USB ID selects its distinct backend");
        Check(listener.ResolveDevice(Record(0x1eaf, 4)) == null, "STM32duino normal runtime PID does not trigger flashing");
        Check(listener.ResolveDevice(Record(0x0483, 0xdf12)) == null, "Near-match STM32 PID is rejected");
        Check(listener.ResolveDevice(Record(0x0483, 0xdf11, false)) == null, "Matching IDs without a DFU interface are rejected");
        Check(listener.ResolveDevice(Record()).ProductString == "STM32 BOOTLOADER", "Live USB product information is retained without inventing a board model");
        Check(Stm32Firmware.Load(firmware, f401, Stm32Protocol.RomDfu).Data.Length == 256, "F401 ROM DFU application accepted");
        Check(Stm32Firmware.Load(firmware, f411, Stm32Protocol.RomDfu).Address == 0x08000000, "F411 ROM DFU uses flash base");
        Check(Stm32Firmware.Load(duinoFirmware, f103, Stm32Protocol.Stm32Duino).Address == 0x08002000, "F103 STM32duino preserves the first 8 KB");
        Check(Stm32Firmware.Load(duinoFirmware, f103cb, Stm32Protocol.Stm32Duino).Address == 0x08002000, "F103CB STM32duino profile supported");
        Check(Stm32Firmware.Load(firmware, f103, Stm32Protocol.RomUart).Address == 0x08000000, "F103 ROM UART uses flash base, not STM32duino offset");
        Reject(() => Stm32Firmware.Load(firmware, f103, Stm32Protocol.RomDfu), "F103 cannot be treated as F4 factory USB DFU");
        Reject(() => Stm32Firmware.Load(duinoFirmware, f401, Stm32Protocol.Stm32Duino), "F4 cannot select the F103 STM32duino backend");
        Reject(() => Stm32Firmware.Load(firmware, f103, Stm32Protocol.Stm32Duino), "Wrong-offset firmware rejected before STM32duino download");
        Reject(() => Stm32Target.Find("atmega32u4"), "AVR MCU selection cannot authorize an STM32 flash");
        Reject(() => Stm32Firmware.Load(Write("empty.bin", Array.Empty<byte>()), f401, Stm32Protocol.RomDfu), "Empty firmware rejected");
        Reject(() => Stm32Firmware.Load(Write("oversize.bin", Image(0x08000000, 256 * 1024 + 1)), f401, Stm32Protocol.RomDfu), "Firmware larger than selected flash rejected");
        Reject(() => Stm32Firmware.Load(Write("wrong-ram.bin", Image(0x08000000, 256, 0x20020000)), f401, Stm32Protocol.RomDfu), "Incompatible RAM vector rejected");
        var invalid = Image(0x08000000); invalid[4] &= 0xfe;
        Reject(() => Stm32Firmware.Load(Write("wrong-vector.bin", invalid), f401, Stm32Protocol.RomDfu), "Non-Thumb reset vector rejected");
        var hex = HexRecord(0, 4, new byte[] { 8, 0 }) + "\n" + HexRecord(0, 0, Image(0x08000000).Take(64).ToArray()) + "\n:00000001FF\n";
        var hexFile = Write("f401.hex", Encoding.ASCII.GetBytes(hex));
        Check(Stm32Firmware.Load(hexFile, f401, Stm32Protocol.RomDfu).Data.Length == 64, "Intel HEX converted with embedded address validation");
        Reject(() => Stm32Firmware.Load(Write("bad.hex", Encoding.ASCII.GetBytes(hex.Replace("FF\n", "FE\n"))), f401, Stm32Protocol.RomDfu), "Bad HEX checksum rejected");
        Reject(() => Stm32Firmware.Load(Write("no-eof.hex", Encoding.ASCII.GetBytes(hex.Split(":00000001FF")[0])), f401, Stm32Protocol.RomDfu), "HEX missing EOF rejected");
        Reject(() => Stm32Firmware.Load(Write("overlap.hex", Encoding.ASCII.GetBytes(hex.Replace(":00000001FF", HexRecord(0, 0, new byte[] { 1 }) + "\n:00000001FF"))), f401, Stm32Protocol.RomDfu), "Overlapping HEX writes rejected");
        Reject(() => Stm32Firmware.Load(hexFile, f103, Stm32Protocol.Stm32Duino), "HEX cannot overwrite the STM32duino bootloader");
        var container = Dfu(Image(0x08000000));
        var dfuFile = Write("f401.dfu", container);
        Check(Stm32Firmware.Load(dfuFile, f401, Stm32Protocol.RomDfu).Data.SequenceEqual(Image(0x08000000)), "Single-target DfuSe container validated and extracted");
        Check(Stm32Firmware.Crc(Encoding.ASCII.GetBytes("123456789")) == 0x340bc6d9, "DfuSe CRC matches independent standard check value");
        var corrupt = (byte[])container.Clone(); corrupt[^1] ^= 1;
        Reject(() => Stm32Firmware.Load(Write("crc.dfu", corrupt), f401, Stm32Protocol.RomDfu), "DfuSe CRC corruption rejected");
        var multi = (byte[])container.Clone(); multi[10] = 2;
        Reject(() => Stm32Firmware.Load(Write("multi.dfu", multi), f401, Stm32Protocol.RomDfu), "Multi-target DfuSe rejected rather than partially flashed");
        Reject(() => Stm32Firmware.Load(dfuFile, f103, Stm32Protocol.Stm32Duino), "DfuSe cannot be sent through generic STM32duino DFU");
        var command = new Stm32DfuDevice(RomDevice()).BuildArguments(new(0x08000000, Image(0x08000000)), "/fixture.bin");
        Check(command.Contains("-d ,0483:df11") && command.Contains("-p \"1-2.3\"") && command.Contains("-a 0") && command.Contains("0x08000000:leave"), "ROM command selects DFU mode, physical port and internal flash");
        var duino = new Stm32DuinoDevice(new KnownHidDevice { VendorId = 0x1eaf, ProductId = 3, Identity = "1-2.3" });
        var duinoArgs = duino.BuildArguments(new(0x08002000, Image(0x08002000)), "/fixture.bin");
        Check(duinoArgs.Contains("-a 2 -R") && !duinoArgs.Contains("-s "), "STM32duino uses alt 2 generic DFU without DfuSe commands");
        var validDuinoHex = HexRecord(0, 4, new byte[] { 8, 0 }) + "\n" + HexRecord(0x2000, 0, Image(0x08002000).Take(64).ToArray()) + "\n:00000001FF\n";
        Check(Stm32Firmware.Load(Write("f103-offset.hex", Encoding.ASCII.GetBytes(validDuinoHex)), f103, Stm32Protocol.Stm32Duino).Address == 0x08002000, "F103 Intel HEX application offset accepted without relocation");
        var simulatedDuino = new SimulatedDuino(); simulatedDuino.Flash(f103.Name, duinoFirmware);
        Check(simulatedDuino.Calls == 1, "STM32duino profile/layout validation reaches the simulated generic DFU backend");
        var report = "Found DFU: [0483:df11] path=\"1-2.3\", alt=0, name=\"@Internal Flash /0x08000000/04*016Kg,01*064Kg,01*128Kg\"";
        Stm32DfuLayout.Validate(report, "1-2.3", f401, Stm32Protocol.RomDfu);
        Check(true, "F401 advertised DFU memory map matches profile");
        Stm32DfuLayout.Validate(report.Replace("01*128Kg", "03*128Kg"), "1-2.3", f411, Stm32Protocol.RomDfu);
        Check(true, "F411 advertised 512 KB memory map matches its profile");
        Reject(() => Stm32DfuLayout.Validate(report, "1-2.3", f411, Stm32Protocol.RomDfu), "F411 profile cannot write a device reporting F401 flash capacity");
        Reject(() => Stm32DfuLayout.Validate(report, "1-9", f401, Stm32Protocol.RomDfu), "DFU preflight rejects a different physical port");
        Reject(() => Stm32DfuLayout.Validate(report.Replace("Kg", "Ka"), "1-2.3", f401, Stm32Protocol.RomDfu), "Protected DFU sectors block writes");
        var mapleReport = "Found DFU: [1eaf:0003] path=\"1-2.3\", alt=2, name=\"STM32duino bootloader v1.0 Upload to Flash 0x8002000\"";
        Stm32DfuLayout.Validate(mapleReport, "1-2.3", f103, Stm32Protocol.Stm32Duino);
        Check(true, "STM32duino alt 2 offset verified before programming");
        Reject(() => Stm32DfuLayout.Validate(mapleReport.Replace("8002000", "8005000"), "1-2.3", f103, Stm32Protocol.Stm32Duino), "Legacy 20 KB Maple offset is not silently treated as 8 KB");
        var added = 0; var removed = 0;
        listener.BootloaderDeviceConnected += _ => added++;
        listener.BootloaderDeviceDisconnected += _ => removed++;
        listener.ReconcileDevices(new() { Record() }); listener.ReconcileDevices(new() { Record() });
        Check(added == 1, "Polling an unchanged device emits one connection");
        listener.ReconcileDevices(new()); listener.ReconcileDevices(new() { Record() });
        Check(added == 2 && removed == 1, "Disconnect/reconnect creates a fresh bootloader session");
        listener.ReconcileDevices(new() { Record(0x1eaf, 4) });
        Check(removed == 2 && added == 2, "Re-enumeration as normal firmware removes the bootloader");
        var mismatchedUsb = new SimulatedRom { Layout = "04*016Kg,01*064Kg,03*128Kg" };
        Reject(() => mismatchedUsb.Flash(f401.Name, firmware), "Live DFU capacity mismatch blocks download");
        Check(mismatchedUsb.Calls == 0, "No download follows an incompatible USB memory map");
        var romFailure = new SimulatedRom { Fail = true };
        Reject(() => romFailure.Flash(f401.Name, firmware), "USB transfer failures propagate to caller");
        Check(!Directory.GetFiles(QMK_Toolbox.Helpers.EmbeddedResourceHelper.ResourceDirectory, "stm32-*.bin").Any(), "Normalized firmware snapshots cleaned after failure");
        var missingTool = new MissingToolRom();
        Reject(() => missingTool.Flash(f401.Name, firmware), "Missing flashing utility is reported without a device write");
        Reject(() => new UsbAspDevice(new KnownHidDevice()).ValidateFirmware("atmega32u4", dfuFile), "DFU file selection cannot bypass other bootloaders' format validation");
        var fullSizeDfu = (byte[])container.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(fullSizeDfu.AsSpan(6), (uint)fullSizeDfu.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(fullSizeDfu.AsSpan(fullSizeDfu.Length - 4), Stm32Firmware.Crc(fullSizeDfu.AsSpan(0, fullSizeDfu.Length - 4)));
        Check(Stm32Firmware.Load(Write("dfu-util-pack.dfu", fullSizeDfu), f401, Stm32Protocol.RomDfu).Data.Length == 256, "DfuSe image-size convention used by official dfuse-pack is accepted");
        var rapid = new UsbListener(); var rapidAdded = 0; var rapidRemoved = 0;
        rapid.BootloaderDeviceConnected += _ => rapidAdded++; rapid.BootloaderDeviceDisconnected += _ => rapidRemoved++;
        var oldAddress = Record(); oldAddress.DeviceAddress = "4";
        var newAddress = Record(); newAddress.DeviceAddress = "5";
        rapid.ReconcileDevices(new() { oldAddress }); rapid.ReconcileDevices(new() { newAddress });
        Check(rapidAdded == 2 && rapidRemoved == 1, "USB address change detects fast same-port re-enumeration between polls");
        var uart = new SimulatedUart(0x423); uart.Flash(f401.Name, firmware);
        Check(uart.Calls.Count == 2 && !uart.Calls[0].Contains("-w ") && uart.Calls[1].Contains("-v -g 0x0"), "UART reads chip ID before verified write and go");
        var wrongUart = new SimulatedUart(0x431);
        Reject(() => wrongUart.Flash(f401.Name, firmware), "UART mismatched chip ID blocks the write");
        Check(wrongUart.Calls.Count == 1, "No write command issued after UART mismatch");
        Reject(() => new Stm32UartDevice("/tmp/not-a-port").ValidateFirmware(f401.Name, firmware), "Arbitrary file cannot be used as a UART port");

        var events = new UsbListener(); var vm = new MainWindowViewModel("", new StubWindow(), events, false);
        vm.Mcus = new() { "atmega32u4" }; vm.SelectedMcuIndex = 0; vm.HexFile = firmware;
        var fake = new SimulatedRom();
        Connect(events, fake); Dispatcher.UIThread.RunJobs();
        Check(!vm.CanFlash && fake.Calls == 0, "Unconfirmed STM32 target disables manual and automatic flash");
        vm.SelectedStm32Target = f103.Name;
        Check(!vm.CanFlash, "F103 selection blocks ROM DFU in GUI");
        vm.SelectedStm32Target = f401.Name;
        Check(vm.CanFlash, "Matching target and validated image enable STM32 flash");
        vm.FlashCommand(); Drain(() => !vm.IsBusy && vm.Log.Contains("50%"));
        Check(fake.Calls == 1 && fake.Image.SequenceEqual(Image(0x08000000)), "GUI executes validated STM32 snapshot through simulated backend");
        Check(vm.Log.Contains("50%"), "Flashing progress reaches the main log");
        events.BootloaderDeviceDisconnected?.Invoke(fake); Dispatcher.UIThread.RunJobs();
        Check(!vm.CanFlash, "Disconnect disables flash controls");
        vm.IsAutoFlash = true; var second = new SimulatedRom();
        Connect(events, second); Dispatcher.UIThread.RunJobs(); Drain(() => !vm.IsBusy);
        Check(second.Calls == 1, "Auto-Flash starts once on a valid reconnect");
        events.BootloaderDeviceDisconnected?.Invoke(second); Dispatcher.UIThread.RunJobs();
        vm.HexFile = duinoFirmware; var incompatible = new SimulatedRom();
        Connect(events, incompatible); Dispatcher.UIThread.RunJobs();
        Check(!vm.CanFlash && incompatible.Calls == 0, "Auto-Flash never writes an incompatible offset");
        vm.IsAutoFlash = false; vm.HexFile = firmware; incompatible.Fail = true;
        vm.FlashCommand(); Drain(() => !vm.IsBusy);
        Check(vm.Log.Contains("simulated transfer failure") && vm.CanFlash, "Failed flash is logged and UI recovers");
        events.BootloaderDeviceDisconnected?.Invoke(incompatible); Dispatcher.UIThread.RunJobs();
        var a = new SimulatedRom(); var b = new SimulatedRom();
        Connect(events, a); Connect(events, b); Dispatcher.UIThread.RunJobs();
        Check(!vm.CanFlash, "Multiple bootloaders block unintended target selection");
        Check(!vm.CanFlashUart, "UART action cannot compete with detected USB bootloaders");
        events.BootloaderDeviceDisconnected?.Invoke(a); events.BootloaderDeviceDisconnected?.Invoke(b); Dispatcher.UIThread.RunJobs();
        var held = new SimulatedRom { Hold = new ManualResetEventSlim(false) };
        Connect(events, held); Dispatcher.UIThread.RunJobs();
        vm.FlashCommand(); Drain(() => held.Calls == 1 && vm.IsBusy);
        events.BootloaderDeviceDisconnected?.Invoke(held); var reconnect = new SimulatedRom();
        vm.IsAutoFlash = true; Connect(events, reconnect); Dispatcher.UIThread.RunJobs();
        Check(reconnect.Calls == 0 && vm.IsBusy, "Disconnect and re-enumeration cannot overlap active writes");
        held.Hold.Set(); Drain(() => !vm.IsBusy && reconnect.Calls == 1);
        Check(reconnect.Calls == 1, "Queued Auto-Flash runs once after the previous session completes");
        events.BootloaderDeviceDisconnected?.Invoke(reconnect); Dispatcher.UIThread.RunJobs();
        vm.SelectedStm32Target = "Select actual STM32 MCU…"; var unconfirmed = new SimulatedRom();
        Connect(events, unconfirmed); Dispatcher.UIThread.RunJobs();
        Check(unconfirmed.Calls == 0 && !vm.CanFlash, "Auto-Flash remains blocked when MCU confirmation is absent");
        Directory.Delete(Root, true);
    }

    private sealed class SimulatedRom : Stm32DfuDevice
    {
        public int Calls; public bool Fail; public byte[] Image; public ManualResetEventSlim Hold;
        public string Layout = "04*016Kg,01*064Kg,01*128Kg";
        public SimulatedRom() : base(RomDevice()) { }
        protected override Task<int> RunProcessAsync(string command, string args)
        {
            if (args.EndsWith(" -l"))
            {
                LastProcessOutput = $"Found DFU: [0483:df11] ver=2200, path=\"1-2.3\", alt=0, name=\"@Internal Flash /0x08000000/{Layout}\", serial=\"fixture\"";
                return Task.FromResult(0);
            }
            Interlocked.Increment(ref Calls);
            // Snapshot path is generated by production code; only this fake reads it.
            var path = args.Split("-D ")[1].Trim().Trim('"'); Image = File.ReadAllBytes(path);
            Hold?.Wait(TimeSpan.FromSeconds(4));
            PrintMessage("Download 50%", MessageType.CommandOutput);
            if (Fail) throw new IOException("simulated transfer failure: permission denied or disconnected");
            return Task.FromResult(0);
        }
    }
    private sealed class SimulatedDuino : Stm32DuinoDevice
    {
        public int Calls;
        public SimulatedDuino() : base(new KnownHidDevice { VendorId = 0x1eaf, ProductId = 3, Identity = "1-2.3" }) { }
        protected override Task<int> RunProcessAsync(string command, string args)
        {
            if (args.EndsWith(" -l"))
                LastProcessOutput = "Found DFU: [1eaf:0003] path=\"1-2.3\", alt=2, name=\"STM32duino bootloader v1.0 Upload to Flash 0x8002000\"";
            else Calls++;
            return Task.FromResult(0);
        }
    }
    private sealed class MissingToolRom : Stm32DfuDevice
    {
        public MissingToolRom() : base(RomDevice()) { }
        protected override Task<int> RunProcessAsync(string command, string args) =>
            throw new FileNotFoundException("simulated missing dfu-util; no device opened");
    }
    private sealed class SimulatedUart : Stm32UartDevice
    {
        public List<string> Calls = new(); private readonly int _chip;
        public SimulatedUart(int chip) : base("/dev/ttyUSB999999") => _chip = chip;
        public override void ValidateFirmware(string target, string file) => Stm32Firmware.Load(file, Stm32Target.Find(target), Stm32Protocol.RomUart);
        protected override Task<int> RunProcessAsync(string command, string args)
        { Calls.Add(args); LastProcessOutput = $"Device ID    : 0x{_chip:x4} (simulated)"; return Task.FromResult(0); }
    }
    private sealed class StubWindow : IWindow
    {
        public void ShowMessage(string message) { }
        public void OnClose() { }
        public Task OnFileOpen() => Task.CompletedTask;
    }
}
