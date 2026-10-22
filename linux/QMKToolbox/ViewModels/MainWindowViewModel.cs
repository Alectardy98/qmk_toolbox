using System;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Avalonia.Threading;
using QMK_Toolbox.Helpers;
using QMK_Toolbox.Stm32;
using QMK_Toolbox.Properties;
using QMK_Toolbox.Usb;
using QMK_Toolbox.Usb.Bootloader;
using QMK_Toolbox.Views;
using ReactiveUI;

namespace QMK_Toolbox.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    internal readonly string Prompt = "Click Open or drag to window to select file";
    private string _hexFile;
    private List<string> _mcus;
    private int _selectedMcu=10;
    private bool _isAutoFlash;
    private readonly StringBuilder _log = new();
    private readonly string _arg;
    private readonly IWindow _mainWindow;
    private readonly UsbListener _usbListener;
    private BootloaderDevice _currentBootloader;
    private bool _canOpenFile = true;
    private bool _canFlash;
    private bool _canReset;
    private bool _canClearEeprom;
    private bool _canAutoFlash = true;

    private readonly bool _persistSettings;
    public MainWindowViewModel(string arg, IWindow mainWindow) : this(arg, mainWindow, new UsbListener(), true) { }

    internal MainWindowViewModel(string arg, IWindow mainWindow, UsbListener listener, bool persistSettings)
    {
        _arg = arg;
        _mainWindow = mainWindow;
        _usbListener = listener;
        _persistSettings = persistSettings;
        _hexFile = string.IsNullOrEmpty(Settings.Default.hexFile) ? Prompt : Settings.Default.hexFile;
        SubscribeToEvents();
    }

    public List<string> Stm32Targets { get; } = new[] { "Select actual STM32 MCU…" }.Concat(Stm32Target.All.Select(x => x.Name)).ToList();
    private string _selectedStm32Target = "Select actual STM32 MCU…";
    public string SelectedStm32Target
    {
        get => _selectedStm32Target;
        set { this.RaiseAndSetIfChanged(ref _selectedStm32Target, value); UpdateControls(); }
    }
    private string _uartPort;
    public string UartPort
    {
        get => _uartPort;
        set { this.RaiseAndSetIfChanged(ref _uartPort, value); UpdateControls(); }
    }
    private string _firmwareStatus = "STM32: select the actual chip before flashing; USB IDs identify the bootloader, not the board.";
    public string FirmwareStatus
    {
        get => _firmwareStatus;
        private set => this.RaiseAndSetIfChanged(ref _firmwareStatus, value);
    }
    private bool _canFlashUart;
    public bool CanFlashUart
    {
        get => _canFlashUart;
        private set => this.RaiseAndSetIfChanged(ref _canFlashUart, value);
    }
    private string TargetFor(BootloaderDevice device) => device.RequiresStm32Target ? SelectedStm32Target :
        Mcus != null && SelectedMcuIndex >= 0 && SelectedMcuIndex < Mcus.Count ? Mcus[SelectedMcuIndex] : null;

    private bool Validate(BootloaderDevice device, out string error)
    {
        try
        {
            if (TargetFor(device) == null) throw new InvalidDataException("Select a target MCU.");
            device.ValidateFirmware(TargetFor(device), HexFile);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { error = ex.Message; return false; }
    }

    public ObservableCollection<string> HidDevices { get; } = new() { "All HID consoles" };
    private string _selectedHidDevice = "All HID consoles";
    public string SelectedHidDevice
    {
        get => _selectedHidDevice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedHidDevice, value);
            _hidConsole.SelectedDevice = value == "All HID consoles" ? null : value;
        }
    }
    private bool _showAllDevices;
    public bool ShowAllDevices
    {
        get => _showAllDevices;
        set => this.RaiseAndSetIfChanged(ref _showAllDevices, value);
    }
    private bool _listenToHid = true;
    public bool ListenToHid
    {
        get => _listenToHid;
        set => this.RaiseAndSetIfChanged(ref _listenToHid, value);
    }
    private bool _busy;
    public bool IsBusy => _busy;
    private readonly List<BootloaderDevice> _devices = new();

    private void UpdateControls()
    {
        CanOpenFile = !_busy;
        CanAutoFlash = !_busy;
        string error = null;
        CanFlash = !_busy && _currentBootloader != null && Validate(_currentBootloader, out error);
        CanReset = !_busy && _currentBootloader?.IsResettable == true &&
            (!_currentBootloader.RequiresStm32Target || Stm32Target.All.Any(x => x.Name == SelectedStm32Target && !x.IsF103));
        CanClearEeprom = !_busy && _currentBootloader?.IsEepromFlashable == true;
        CanFlashUart = !_busy && _devices.Count == 0 && Validate(new Stm32UartDevice(UartPort), out _);
        FirmwareStatus = _currentBootloader?.RequiresStm32Target == true
            ? error ?? $"Validated for {SelectedStm32Target}. The actual chip and QMK board build must match your selection."
            : "STM32: select the actual chip. UART is manual; never inferred from a USB adapter.";
    }

    public bool CanAutoFlash
    {
        get => _canAutoFlash;
        set => this.RaiseAndSetIfChanged(ref _canAutoFlash, value);
    }

    public bool CanOpenFile
    {
        get => _canOpenFile;
        set => this.RaiseAndSetIfChanged(ref _canOpenFile, value);
    }

    public bool CanFlash
    {
        get => _canFlash;
        set => this.RaiseAndSetIfChanged(ref _canFlash, value);
    }

    public bool CanReset
    {
        get => _canReset;
        set => this.RaiseAndSetIfChanged(ref _canReset, value);
    }

    public bool CanClearEeprom
    {
        get => _canClearEeprom;
        set => this.RaiseAndSetIfChanged(ref _canClearEeprom, value);
    }

    public List<string> Mcus
    {
        get => _mcus;
        set => this.RaiseAndSetIfChanged(ref _mcus, value);
    }

    public string Log => _log.ToString();

    private void AddToLog(string text, bool notify=true)
    {
        if (_log.Length > 500000) _log.Remove(0, _log.Length - 250000);
        _log.AppendLine(text);
        if (notify)
            this.RaisePropertyChanged(nameof(Log));
    }

    public bool IsAutoFlash
    {
        get => _isAutoFlash;
        set => this.RaiseAndSetIfChanged(ref _isAutoFlash, value);
    }

    public int SelectedMcuIndex
    {
        get => _selectedMcu;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMcu, value);
            if (Mcus != null && value >= 0 && value < Mcus.Count)
            { if (_persistSettings) { Settings.Default.targetSetting = Mcus[value]; Settings.Default.Save(); } }
        }
    }

    private AssemblyFileVersionAttribute VersionAttribute =>
        typeof(MainWindowViewModel).Assembly
            .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).FirstOrDefault()
                as AssemblyFileVersionAttribute;

    public void InitUi()
    {
        Mcus = PopulateMcuListFromResourceFile();
        var savedMcu = Mcus.IndexOf(Settings.Default.targetSetting);
        if (savedMcu >= 0) SelectedMcuIndex = savedMcu;
        UpdateHexFileFromCommandLineArguments();
        RestoreLastSavedHexFile();
        ShowInitialLogMessages();

        // Extract the bundled AVR flashing tool so Linux users do not need
        // avrdude installed system-wide.
        foreach (var resource in new[]
        {
            "avrdude",
            "avrdude.conf",
            "reset.eep",
            "reset_left.eep",
            "reset_right.eep",
            "dfu-programmer",
            "dfu-util",
            "hid_bootloader_cli",
            "teensy_loader_cli",
            "mdloader",
            "wb32-dfu-updater_cli",
            "bootloadHID",
            "libusb.so",
            "libhidapi-libusb.so",
            "libftdi1.so"
        })
        {
            try { EmbeddedResourceHelper.ExtractResource(resource); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AddToLog($"Could not prepare {resource}: {ex.Message}. Rebuild or reinstall the application."); }
        }
        _usbListener.Start();
        _hidConsole.Start();
        UpdateControls();
    }

    private readonly Helpers.HidConsole _hidConsole = new();

    private void SubscribeToEvents()
    {
        _hidConsole.AvailableDevicesChanged += devices => Dispatcher.UIThread.Post(() =>
        {
            var selected = SelectedHidDevice;
            HidDevices.Clear();
            HidDevices.Add("All HID consoles");
            foreach (var device in devices) HidDevices.Add(device);
            SelectedHidDevice = HidDevices.Contains(selected) ? selected : "All HID consoles";
        });
        _hidConsole.Output += text => Dispatcher.UIThread.Post(() => { if (ListenToHid) AddToLog(text); });
        _usbListener.DeviceChanged += (device, connected) => Dispatcher.UIThread.Post(() =>
        {
            if (ShowAllDevices) AddToLog($"USB {(connected ? "connected" : "disconnected")}: {device.VendorId:x4}:{device.ProductId:x4} [{device.Identity}]");
        });
        _usbListener.OutputReceived += (_, data, _) => Dispatcher.UIThread.Post(() => AddToLog(data));
        _usbListener.BootloaderDeviceConnected += device => Dispatcher.UIThread.Post(() =>
        {
            _devices.Add(device);
            _currentBootloader = _devices.Count == 1 ? device : null;
            AddToLog($"Connected: {device.Name} {device}");
            if (_devices.Count > 1) AddToLog("Multiple bootloaders connected. Disconnect all but the target before flashing.");
            UpdateControls();
            if (_isAutoFlash && CanFlash) FlashCommand();
        });
        _usbListener.BootloaderDeviceDisconnected += device => Dispatcher.UIThread.Post(() =>
        {
            _devices.Remove(device);
            _currentBootloader = _devices.Count == 1 ? _devices[0] : null;
            AddToLog($"Disconnected: {device.Name} {device}");
            UpdateControls();
        });
    }

    public void Dispose()
    {
        _usbListener.Stop();
        _hidConsole.Dispose();
        EmbeddedResourceHelper.Cleanup();
    }

    private void RestoreLastSavedHexFile()
    {
        if (Settings.Default.hexFile != null && string.IsNullOrEmpty(_arg))
        {
            HexFile = Settings.Default.hexFile;
        }
    }

    private void UpdateHexFileFromCommandLineArguments()
    {
        if (string.IsNullOrEmpty(_arg))
            return;
        var extension = Path.GetExtension(_arg)?.ToLower();
        if (extension is ".hex" or ".bin" or ".dfu")
        {
            HexFile = _arg;
        }
        else
        {
            _mainWindow
                .ShowMessage("QMK Toolbox doesn't support this kind of file, supported firmware extensions are .hex, .bin, and .dfu (STM32 ROM DFU only).");
        }
    }

    private void ShowInitialLogMessages()
    {
        AddToLog($"* QMK Toolbox {VersionAttribute.Version} (https://qmk.fm/toolbox)", false);
        AddToLog("* Supported bootloaders:", false);
        AddToLog(
            "* - ARM DFU (APM32, Kiibohd, STM32, STM32duino) and RISC-V DFU (GD32V) via dfu-util (http://dfu-util.sourceforge.net/)",
            false);
        AddToLog("* - Atmel/LUFA/QMK DFU via dfu-programmer (http://dfu-programmer.github.io/)", false);
        AddToLog("* - Atmel SAM-BA (Massdrop) via Massdrop Loader (https://github.com/massdrop/mdloader)", false);
        AddToLog(
            "* - BootloadHID (Atmel, PS2AVRGB) via bootloadHID (https://www.obdev.at/products/vusb/bootloadhid.html)",
            false);
        AddToLog("* - Caterina (Arduino, Pro Micro) via avrdude (http://nongnu.org/avrdude/)", false);
        AddToLog("* - HalfKay (Teensy, Ergodox EZ) via Teensy Loader (https://pjrc.com/teensy/loader_cli.html)", false);
        AddToLog("* - LUFA/QMK HID via hid_bootloader_cli (https://github.com/abcminiuser/lufa)", false);
        AddToLog("* - WB32 DFU via wb32-dfu-updater_cli (https://github.com/WestberryTech/wb32-dfu-updater)", false);
        AddToLog("* - LUFA Mass Storage", false);
        AddToLog("* Supported ISP flashers:", false);
        AddToLog("* - AVRISP (Arduino ISP)", false);
        AddToLog("* - USBasp (AVR ISP)", false);
        AddToLog("* - USBTiny (AVR Pocket)", true);
    }

    public string HexFile
    {
        get => _hexFile;
        set
        {
            if (File.Exists(value)) value = Path.GetFullPath(value);
            this.RaiseAndSetIfChanged(ref _hexFile, value);
            _hexFile = value;
            UpdateControls();
            if (_persistSettings) { Settings.Default.hexFile = value; Settings.Default.Save(); }
        }
    }

    public void CloseCommand()
    {
        _mainWindow.OnClose();
    }

    public void OpenFileCommand()
    {
        if (CanOpenFile) _ = _mainWindow.OnFileOpen();
    }

    public void AutoFlashMenuCommand() => IsAutoFlash = !IsAutoFlash;

    [DependsOn(nameof(CanAutoFlash))]
    public bool CanAutoFlashMenuCommand()
    {
        return  _canAutoFlash;
    }

    [DependsOn(nameof(CanOpenFile))]
    public bool CanOpenFileCommand(object _)
    {
        return _canOpenFile;
    }

    private async Task ExecuteOperation(Action<BootloaderDevice, string> operation, bool requiresFile, BootloaderDevice manual = null)
    {
        var device = manual ?? _currentBootloader;
        if (_busy || device == null || (manual != null && _devices.Count != 0)) return;
        if (requiresFile && !Validate(device, out var error)) { AddToLog($"Cannot flash: {error}"); return; }
        var target = TargetFor(device);
        if (target == null) return;
        _busy = true;
        UpdateControls();
        try { await Task.Run(() => operation(device, target)); }
        catch (Exception ex) { AddToLog($"Operation failed: {ex.GetBaseException().Message}"); }
        finally
        {
            _busy = false;
            UpdateControls();
            if (manual == null && IsAutoFlash && CanFlash && _currentBootloader != device) FlashCommand();
        }
    }

    public async void FlashStm32UartCommand()
    {
        if (!CanFlashUart) return;
        var device = new Stm32UartDevice(UartPort);
        device.OutputReceived = (_, data, _) => AddToLog(data);
        var file = HexFile;
        await ExecuteOperation((d, target) => d.Flash(target, file), true, device);
    }

    public async void FlashCommand()
    {
        var file = HexFile;
        await ExecuteOperation((device, mcu) => device.Flash(mcu, file), true);
    }

    [DependsOn(nameof(CanFlash))]
    public bool CanFlashCommand(object _) => CanFlash;

    private async Task ClearEeprom(string image)
    {
        if (!CanClearEeprom) return;
        await ExecuteOperation((device, mcu) => device.FlashEeprom(mcu,
            Path.Combine(EmbeddedResourceHelper.ResourceDirectory, image)), false);
    }

    public async void ClearEEPromCommand() => await ClearEeprom("reset.eep");
    public async void ClearLeftEepromCommand() => await ClearEeprom("reset_left.eep");
    public async void ClearRightEepromCommand() => await ClearEeprom("reset_right.eep");
    [DependsOn(nameof(CanClearEeprom))]
    public bool CanClearEEPromCommand(object _) => CanClearEeprom;

    public async void ExitDfuCommand()
    {
        if (!CanReset) return;
        await ExecuteOperation((device, mcu) => device.Reset(mcu), false);
    }

    [DependsOn(nameof(CanReset))]
    public bool CanExitDfuCommand(object _) => CanReset;

    public void ClearLogCommand() { _log.Clear(); this.RaisePropertyChanged(nameof(Log)); }

    private List<string> PopulateMcuListFromResourceFile()
    {
        List<string> mcuList = new();
        var microcontrollers = EmbeddedResourceHelper.GetResourceContent("mcu-list.txt").Split('\n');

        foreach (var microcontroller in microcontrollers)
        {
            if (microcontroller.Length <= 0)
                continue;
            var parts = microcontroller.Split(':');
            mcuList.Add(parts[0]);
        }

        return mcuList;
    }
}
