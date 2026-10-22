using System;
using Avalonia.Threading;
// ReSharper disable StringLiteralTypo
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace QMK_Toolbox.Usb.Bootloader;

internal abstract class BootloaderDevice
{
    public delegate void FlashOutputReceivedDelegate(BootloaderDevice device, string data, MessageType type);

    public FlashOutputReceivedDelegate OutputReceived;

    public BootloaderDevice(KnownHidDevice d)
    {
        KnownHidDevice = d;
    }

    private KnownHidDevice KnownHidDevice { get; }

    public string Identity => KnownHidDevice.Identity;

    public ushort VendorId => KnownHidDevice.VendorId;

    public ushort ProductId => KnownHidDevice.ProductId;

    public ushort RevisionBcd => KnownHidDevice.RevisionBcd;

    public string ManufacturerString => KnownHidDevice.VendorName;

    public string ProductString => KnownHidDevice.ProductName;

    public bool IsEepromFlashable { get; protected set; }

    public bool IsResettable { get; protected set; }

    public BootloaderType Type { get; protected set; }

    public string Name { get; protected set; }

    public override string ToString()
    {
        return KnownHidDevice.ToString();
    }

    public virtual bool RequiresStm32Target => false;

    public virtual void ValidateFirmware(string target, string file)
    {
        if (!File.Exists(file) || !(Path.GetExtension(file).ToLowerInvariant() is ".hex" or ".bin"))
            throw new InvalidDataException("Select an existing .hex or .bin firmware file for this bootloader.");
    }

    public virtual void Flash(string mcu, string file)
    {
        throw new NotImplementedException();
    }

    public virtual void FlashEeprom(string mcu, string file)
    {
        throw new NotImplementedException();
    }
    
    public virtual void Reset(string mcu)
    {
        throw new NotImplementedException();
    }

    protected static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '\"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('\"').ToString();
    }

    protected string LastProcessOutput { get; set; } = "";

    protected virtual async Task<int> RunProcessAsync(string command, string args)
    {
        command = Path.GetFileName(command);
        var directory = Helpers.EmbeddedResourceHelper.ResourceDirectory;
        var executable = Path.Combine(directory, command);
        if (!File.Exists(executable) && command == "stm32flash")
        {
            executable = FindSystemTool(command) ?? throw new FileNotFoundException(
                "Optional UART utility stm32flash is missing. Install stm32flash with your distribution package manager; no serial port was opened.");
        }
        if (!File.Exists(executable)) throw new FileNotFoundException($"Flashing tool {command} is missing; rebuild/reinstall QMK Toolbox.", executable);
        if (command == "avrdude") args = $"-C \"{Path.Combine(directory, "avrdude.conf")}\" {args}";
        PrintMessage($"> {command} {args}", MessageType.Command);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, args)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        var capture = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (capture) capture.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (capture) capture.AppendLine(e.Data); };
        process.OutputDataReceived += ProcessOutput;
        process.ErrorDataReceived += ProcessErrorOutput;
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException($"Cannot start {command}: {ex.Message}. Check executable permissions and native library dependencies; rebuild/reinstall the utility.", ex);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = new System.Threading.CancellationTokenSource(System.TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (System.OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException($"{command} timed out");
        }
        process.WaitForExit(); // Drain asynchronous output before disposing.
        LastProcessOutput = capture.ToString();
        if (process.ExitCode != 0) throw new IOException($"{command} failed (exit {process.ExitCode}). See tool output above. Check the boot mode, firmware target, and QMK udev/serial permissions; reconnect after installing rules.");
        return process.ExitCode;
    }

    private static string FindSystemTool(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory)) continue;
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private void ProcessOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) PrintMessage(e.Data, MessageType.CommandOutput);
    }

    private void ProcessErrorOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) PrintMessage(e.Data, MessageType.CommandError);
    }

    protected void PrintMessage(string message, MessageType type)
    {
        Dispatcher.UIThread.Post(() => OutputReceived?.Invoke(this, message, type));
    }
}
