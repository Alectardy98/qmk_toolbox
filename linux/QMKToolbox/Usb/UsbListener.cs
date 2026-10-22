using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;

using QMK_Toolbox.Usb.Bootloader;

// ReSharper disable StringLiteralTypo

namespace QMK_Toolbox.Usb;

internal class UsbListener
{
    public delegate void BootloaderDeviceEventDelegate(BootloaderDevice device);

    public delegate void FlashOutputReceivedDelegate(BootloaderDevice device, string data, MessageType type);

    private readonly List<KnownHidDevice> _attachedDevices = new();
    private readonly List<KnownHidDevice> _knownDevices = new();

    private Task _backgroundTask;

    public BootloaderDeviceEventDelegate BootloaderDeviceConnected;
    public BootloaderDeviceEventDelegate BootloaderDeviceDisconnected;

    public FlashOutputReceivedDelegate OutputReceived;
    public event Action<UsbDeviceNameRecord, bool> DeviceChanged;

    public UsbListener()
    {
        PopulateKnownDevices();
    }

    public void Start()
    {
        _backgroundTask = new Task(CheckKnownDevicesConnectionState, TaskCreationOptions.LongRunning);
        _backgroundTask.Start();
    }


    private void FlashOutputReceived(BootloaderDevice device, string data, MessageType type)
    {
        OutputReceived?.Invoke(device, data, type);
    }

    private void GetNewUsbDevices(List<UsbDeviceNameRecord> devices)
    {
        foreach (var path in Directory.GetDirectories("/sys/bus/usb/devices"))
        {
            try
            {
                var vendor = Path.Combine(path, "idVendor");
                if (!File.Exists(vendor)) continue;
                devices.Add(new UsbDeviceNameRecord
                {
                    Identity = Path.GetFileName(path),
                    DeviceAddress = ReadOptional(path, "devnum"),
                    ManufacturerName = ReadOptional(path, "manufacturer"),
                    ProductName = ReadOptional(path, "product"),
                    SerialNumber = ReadOptional(path, "serial"),
                    HasDfuInterface = Directory.GetDirectories("/sys/bus/usb/devices", Path.GetFileName(path) + ":*")
                        .Any(x => ReadOptional(x, "bInterfaceClass") == "fe" && ReadOptional(x, "bInterfaceSubClass") == "01"),
                    VendorId = ushort.Parse(File.ReadAllText(vendor).Trim(), NumberStyles.HexNumber),
                    ProductId = ushort.Parse(File.ReadAllText(Path.Combine(path, "idProduct")).Trim(), NumberStyles.HexNumber),
                    DeviceRevisionBcd = ushort.Parse(File.ReadAllText(Path.Combine(path, "bcdDevice")).Trim(), NumberStyles.HexNumber)
                });
            }
            catch (IOException) { } // A device may disappear while reading its attributes.
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { } // Ignore an incomplete descriptor snapshot.
            catch (OverflowException) { }
        }
    }

    private static string ReadOptional(string directory, string attribute)
    {
        try { return File.ReadAllText(Path.Combine(directory, attribute)).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private readonly CancellationTokenSource _stop = new();

    public void Stop() => _stop.Cancel();

    private void CheckKnownDevicesConnectionState()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var current = new List<UsbDeviceNameRecord>();
                GetNewUsbDevices(current);
                ReconcileDevices(current);
                _stop.Token.WaitHandle.WaitOne(500);
            }
        }
        catch (Exception ex) { OutputReceived?.Invoke(null, $"USB monitor stopped: {ex.Message}", MessageType.Error); }

    }

    private List<UsbDeviceNameRecord> _previous = new();
    private readonly Dictionary<string, BootloaderDevice> _attached = new();

    internal void ReconcileDevices(List<UsbDeviceNameRecord> current)
    {
        foreach (var item in _previous.Except(current, new UsbDeviceRecordComparer()))
        {
            DeviceChanged?.Invoke(item, false);
            if (_attached.Remove(item.Identity, out var device)) BootloaderDeviceDisconnected?.Invoke(device);
        }
        foreach (var item in current.Except(_previous, new UsbDeviceRecordComparer()))
        {
            DeviceChanged?.Invoke(item, true);
            var device = ResolveDevice(item);
            if (device == null) continue;
            device.OutputReceived = FlashOutputReceived;
            _attached[item.Identity] = device;
            BootloaderDeviceConnected?.Invoke(device);
        }
        _previous = current.ToList();
    }

    internal BootloaderDevice ResolveDevice(UsbDeviceNameRecord item)
    {
        var known = _knownDevices.Where(x => x.VendorId == item.VendorId && x.ProductId == item.ProductId)
            .OrderByDescending(x => x.RevisionBcd == item.DeviceRevisionBcd).FirstOrDefault();
        if (known == null) return null;
        if ((known.BootloaderType is BootloaderType.Stm32Dfu or BootloaderType.Stm32Duino) && !item.HasDfuInterface)
            return null;
        return CreateDevice(new KnownHidDevice
        {
            Identity = item.Identity, VendorId = known.VendorId, ProductId = known.ProductId,
            RevisionBcd = item.DeviceRevisionBcd, VendorName = item.ManufacturerName ?? known.VendorName,
            ProductName = item.ProductName ?? known.ProductName, SerialNumber = item.SerialNumber,
            BootloaderType = known.BootloaderType
        });
    }

    private static BootloaderDevice CreateDevice(KnownHidDevice device)
    {
        switch (device.BootloaderType)
        {
            case BootloaderType.Apm32Dfu:
                return new Apm32DfuDevice(device);
            case BootloaderType.AtmelDfu:
            case BootloaderType.QmkDfu:
                return new AtmelDfuDevice(device);
            case BootloaderType.AtmelSamBa:
                return new AtmelSamBaDevice(device);
            case BootloaderType.AvrIsp:
                return new AvrIspDevice(device);
            case BootloaderType.BootloadHid:
                return new BootloadHidDevice(device);
            case BootloaderType.Caterina:
                return new CaterinaDevice(device);
            case BootloaderType.Gd32VDfu:
                return new Gd32VDfuDevice(device);
            case BootloaderType.HalfKay:
                return new HalfKayDevice(device);
            case BootloaderType.KiibohdDfu:
                return new KiibohdDfuDevice(device);
            case BootloaderType.LufaHid:
            case BootloaderType.QmkHid:
                return new LufaHidDevice(device);
            case BootloaderType.LufaMs:
                return new LufaMsDevice(device);
            case BootloaderType.Stm32Dfu:
                return new Stm32DfuDevice(device);
            case BootloaderType.Stm32Duino:
                return new Stm32DuinoDevice(device);
            case BootloaderType.UsbAsp:
                return new UsbAspDevice(device);
            case BootloaderType.UsbTinyIsp:
                return new UsbTinyIspDevice(device);
            case BootloaderType.Wb32Dfu:
                return new Wb32DfuDevice(device);
        }
        return null;
    }

    private void PopulateKnownDevices()
    {
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x6124,
            VendorName = "Atmel", ProductName = "SAM-BA", BootloaderType = BootloaderType.AtmelSamBa
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2045,
            VendorName = "Amtel", ProductName = "Bootloader", BootloaderType = BootloaderType.LufaMs
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2067, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "Bootloader QmkHid", BootloaderType = BootloaderType.QmkHid
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2067,
            VendorName = "Amtel", ProductName = "Bootloader QmkHid", BootloaderType = BootloaderType.LufaHid
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2fef,
            VendorName = "Amtel", ProductName = "ATMega16U2", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff0,
            VendorName = "Amtel", ProductName = "ATmega32U2", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff3,
            VendorName = "Amtel", ProductName = "ATmega16U4", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff4,
            VendorName = "Amtel", ProductName = "ATmega32U4", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff9,
            VendorName = "Amtel", ProductName = "AT90USB64", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ffa,
            VendorName = "Amtel", ProductName = "AT90USB64r", BootloaderType = BootloaderType.AtmelDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ffb,
            VendorName = "Amtel", ProductName = "AT90USB128", BootloaderType = BootloaderType.AtmelDfu
        });
        //
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2067, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "Bootloader QmkHid", BootloaderType = BootloaderType.LufaHid
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2fef, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "ATMega16U2", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff0, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "ATmega32U2", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff3, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "ATmega16U4", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff4, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "ATmega32U4", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ff9, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "AT90USB64", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ffa, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "AT90USB64r", BootloaderType = BootloaderType.QmkDfu
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x03eb, ProductId = 0x2ffb, RevisionBcd = 0x0936,
            VendorName = "Amtel", ProductName = "AT90USB128", BootloaderType = BootloaderType.QmkDfu
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x0483, ProductId = 0xdf11,
            VendorName = "STMicroelectronics", ProductName = "STM Device in DFU Mode",
            BootloaderType = BootloaderType.Stm32Dfu
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1209, ProductId = 0x2302,
            VendorName = "Generic", ProductName = "Keyboardio Atreus 2 Bootloader",
            BootloaderType = BootloaderType.Caterina
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x16c0, ProductId = 0x0478,
            VendorName = "Van Ooijen Technische Informatica", ProductName = "USBasp ISP Programmer",
            BootloaderType = BootloaderType.HalfKay
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x16c0, ProductId = 0x0483,
            VendorName = "Van Ooijen Technische Informatica", ProductName = "Teensyduino Serial",
            BootloaderType = BootloaderType.AvrIsp
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x16c0, ProductId = 0x05dc,
            VendorName = "Van Ooijen Technische Informatica", ProductName = "USBasp ISP Programmer",
            BootloaderType = BootloaderType.UsbAsp
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x16c0, ProductId = 0x05df,
            VendorName = "Van Ooijen Technische Informatica", ProductName = "SharedID for USB",
            BootloaderType = BootloaderType.BootloadHid
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1781, ProductId = 0x0c9f,
            VendorName = "Mechanique", ProductName = "USB Tiny",
            BootloaderType = BootloaderType.UsbTinyIsp
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1b4f, ProductId = 0x9203,
            VendorName = "Spark Fun Electronics", ProductName = "Pro Micro 3V3/8MHz",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1b4f, ProductId = 0x9205,
            VendorName = "Spark Fun Electronics", ProductName = "Pro Micro 5V/16MHz",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1b4f, ProductId = 0x9207,
            VendorName = "Spark Fun Electronics", ProductName = "LilyPad 3V3/8MHz (and some Pro Micro clones)",
            BootloaderType = BootloaderType.Caterina
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1c11, ProductId = 0xb007,
            VendorName = "Input Club Inc.", ProductName = "Kiibohd DFU",
            BootloaderType = BootloaderType.KiibohdDfu
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1eaf, ProductId = 0x0003,
            VendorName = "Leaf Labs", ProductName = "STM32duino USB bootloader",
            BootloaderType = BootloaderType.Stm32Duino
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x1ffb, ProductId = 0x0101,
            VendorName = "Pololu Corporation", ProductName = "STM32Duino",
            BootloaderType = BootloaderType.Caterina
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x2341, ProductId = 0x0036,
            VendorName = "Arduino SA", ProductName = "Arduino Leonardo (bootloader)",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x2341, ProductId = 0x0037,
            VendorName = "Arduino SA", ProductName = "Arduino Micro (bootloader)",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x2a03, ProductId = 0x0036,
            VendorName = "dog hunter AG", ProductName = "Arduino Leonardo (bootloader)",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x2a03, ProductId = 0x0037,
            VendorName = "dog hunter AG", ProductName = "Arduino Micro (bootloader)",
            BootloaderType = BootloaderType.Caterina
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x239a, ProductId = 0x000c,
            VendorName = "AdaFruit", ProductName = "Feather 32U4",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x239a, ProductId = 0x000d,
            VendorName = "AdaFruit", ProductName = "ItsyBitsy 32U4 3V3/8MHz",
            BootloaderType = BootloaderType.Caterina
        });
        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x239a, ProductId = 0x000e,
            VendorName = "AdaFruit", ProductName = "ItsyBitsy 32U4 5V/16MHz",
            BootloaderType = BootloaderType.Caterina
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x28e9, ProductId = 0x0189,
            VendorName = "GDMicroelectronics", ProductName = "GD32 DFU Bootloader (Longan Nano)",
            BootloaderType = BootloaderType.Gd32VDfu
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x314b, ProductId = 0x0106,
            VendorName = "Geehy Semiconductor Co. Ltd.", ProductName = "APM32 DFU Bootloader",
            BootloaderType = BootloaderType.Apm32Dfu
        });

        _knownDevices.Add(new KnownHidDevice
        {
            VendorId = 0x342d, ProductId = 0xdfa0,
            VendorName = "WestBerryTech", ProductName = "WB32 DFU Bootloader",
            BootloaderType = BootloaderType.Wb32Dfu
        });
    }
}