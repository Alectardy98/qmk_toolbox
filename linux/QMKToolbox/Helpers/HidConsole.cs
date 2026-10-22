using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QMK_Toolbox.Helpers;

// hidraw leaves the keyboard interface bound to its normal kernel driver.
internal sealed class HidConsole : IDisposable
{
    public event Action<string> Output;
    public event Action<string[]> AvailableDevicesChanged;
    public volatile string SelectedDevice;
    private readonly CancellationTokenSource _stop = new();
    private Task _worker;
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int read(int fd, byte[] buffer, int count);
    [DllImport("libc")] private static extern int close(int fd);

    // HID short items: preserve global usage-page state through PUSH/POP;
    // a console collection must carry PJRC's page and usage together.
    internal static bool IsConsoleDescriptor(byte[] descriptor)
    {
        uint page = 0, usage = 0;
        var pages = new Stack<uint>();
        for (var offset = 0; offset < descriptor.Length;)
        {
            var prefix = descriptor[offset++];
            if (prefix == 0xfe)
            {
                if (offset + 2 > descriptor.Length) return false;
                var length = descriptor[offset];
                offset += 2 + length;
                if (offset > descriptor.Length) return false;
                continue;
            }
            var size = prefix & 3;
            if (size == 3) size = 4;
            if (offset + size > descriptor.Length) return false;
            uint value = 0;
            for (var n = 0; n < size; n++) value |= (uint)descriptor[offset++] << (8 * n);
            var tag = prefix & 0xfc;
            if (tag == 0x04) page = value;
            else if (tag == 0xa4) pages.Push(page);
            else if (tag == 0xb4) { if (pages.Count == 0) return false; page = pages.Pop(); }
            else if (tag == 0x08) usage = size == 4 ? value : (page << 16) | value;
            else if ((prefix & 0x0c) == 0)
            {
                if (tag == 0xa0 && usage == 0xff310074) return true;
                usage = 0;
            }
        }
        return false;
    }

    public void Start() => _worker = Task.Run(Run);

    private void Run()
    {
        var devices = new Dictionary<string, (int Fd, Decoder Decoder, StringBuilder Pending)>();
        var denied = new HashSet<string>();
        var previousDevices = Array.Empty<string>();
        var bytes = new byte[4096];
        var chars = new char[4096];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var paths = Directory.Exists("/sys/class/hidraw")
                    ? Directory.GetDirectories("/sys/class/hidraw") : Array.Empty<string>();
                foreach (var removed in devices.Keys.Except(paths).ToArray())
                {
                    close(devices[removed].Fd);
                    devices.Remove(removed);
                    Output?.Invoke($"HID console disconnected: {Path.GetFileName(removed)}");
                }
                foreach (var path in paths)
                {
                    if (devices.ContainsKey(path)) continue;
                    try
                    {
                        if (!IsConsoleDescriptor(File.ReadAllBytes(Path.Combine(path, "device/report_descriptor")))) continue;
                        var node = "/dev/" + Path.GetFileName(path);
                        var fd = open(node, 0x800); // O_RDONLY | O_NONBLOCK
                        if (fd < 0)
                        {
                            if (denied.Add(path)) Output?.Invoke($"Cannot open HID console {node}: install QMK udev rules and reconnect.");
                            continue;
                        }
                        devices[path] = (fd, Encoding.UTF8.GetDecoder(), new StringBuilder());
                        denied.Remove(path);
                        Output?.Invoke($"HID console connected: {node}");
                    }
                    catch (IOException) { } // Device may disappear during discovery.
                    catch (UnauthorizedAccessException) { }
                }
                var currentDevices = devices.Keys.Select(Path.GetFileName).OrderBy(x => x).ToArray();
                if (!currentDevices.SequenceEqual(previousDevices))
                {
                    AvailableDevicesChanged?.Invoke(currentDevices);
                    previousDevices = currentDevices;
                }
                foreach (var pair in devices.ToArray())
                {
                    // Bound work per poll so a busy console cannot prevent shutdown.
                    for (var report = 0; report < 32; report++)
                    {
                        var count = read(pair.Value.Fd, bytes, bytes.Length);
                        if (count <= 0) break;
                        var length = pair.Value.Decoder.GetChars(bytes, 0, count, chars, 0);
                        var text = new string(chars, 0, length).Replace("\0", "");
                        var pending = pair.Value.Pending;
                        foreach (var character in text)
                        {
                            if (character == '\n' || pending.Length >= 16384)
                            {
                                if (SelectedDevice == null || SelectedDevice == Path.GetFileName(pair.Key))
                                    Output?.Invoke($"[{Path.GetFileName(pair.Key)}] {pending.ToString().TrimEnd('\r')}");
                                pending.Clear();
                            }
                            if (character != '\n') pending.Append(character);
                        }
                    }
                }
                _stop.Token.WaitHandle.WaitOne(100);
            }
        }
        catch (Exception ex) { Output?.Invoke($"HID console stopped: {ex.Message}"); }
        finally { foreach (var device in devices.Values) close(device.Fd); }
    }

    public void Dispose() { _stop.Cancel(); _worker?.Wait(TimeSpan.FromSeconds(2)); }
}
