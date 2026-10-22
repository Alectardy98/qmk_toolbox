using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QMK_Toolbox.Stm32;

// Validation is conservative: application images only, never option bytes,
// bootloader installation, arbitrary address spaces, or multi-target containers.
internal sealed record Stm32Firmware(uint Address, byte[] Data)
{
    internal static Stm32Firmware Load(string file, Stm32Target target, Stm32Protocol protocol)
    {
        var address = target.ApplicationAddress(protocol);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length == 0 || info.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Firmware is missing, empty, or exceeds the STM32 input limit (4 MB).");
        var bytes = File.ReadAllBytes(file);
        Stm32Firmware image = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".bin" => new(address, bytes),
            ".hex" => ParseHex(Encoding.ASCII.GetString(bytes), address, target),
            ".dfu" when protocol == Stm32Protocol.RomDfu => ParseDfu(bytes),
            _ => throw new InvalidDataException(protocol == Stm32Protocol.RomDfu
                ? "STM32 ROM DFU accepts .bin, Intel .hex, or a single-target DfuSe .dfu application image."
                : "This STM32 backend accepts .bin and Intel .hex application images; DfuSe .dfu is for ROM DFU only.")
        };
        if (image.Address != address)
            throw new InvalidDataException($"Image starts at 0x{image.Address:x8}; this bootloader requires an application linked at 0x{address:x8}.");
        if (image.Data.Length < 8 || (ulong)address + (uint)image.Data.Length > (ulong)Stm32Target.FlashBase + (uint)target.FlashBytes)
            throw new InvalidDataException("Firmware does not fit the selected MCU's application flash region.");
        var stack = BinaryPrimitives.ReadUInt32LittleEndian(image.Data);
        var reset = BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(4));
        if (stack <= Stm32Target.RamBase || stack > Stm32Target.RamBase + target.RamBytes || (stack & 3) != 0 ||
            (reset & 1) == 0 || (reset & ~1u) < address + 8 || (ulong)(reset & ~1u) >= (ulong)address + (uint)image.Data.Length)
            throw new InvalidDataException("Firmware has incompatible STM32 application vectors (RAM, reset address, or bootloader offset). Check the MCU and linker configuration.");
        return image;
    }

    private static Stm32Firmware ParseHex(string text, uint start, Stm32Target target)
    {
        var memory = new SortedDictionary<uint, byte>();
        uint upper = 0;
        var ended = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (ended || !line.StartsWith(':') || line.Length % 2 != 1)
                throw new InvalidDataException("Invalid Intel HEX record or data after EOF.");
            byte[] record;
            try { record = Convert.FromHexString(line.Substring(1)); }
            catch (FormatException) { throw new InvalidDataException("Invalid Intel HEX digits."); }
            if (record.Length < 5 || record.Length != record[0] + 5 || (record.Sum(x => (int)x) & 255) != 0)
                throw new InvalidDataException("Intel HEX record length or checksum is invalid.");
            var offset = (uint)(record[1] << 8 | record[2]);
            switch (record[3])
            {
                case 0:
                    for (var i = 0; i < record[0]; i++)
                    {
                        var address = (ulong)upper + offset + (uint)i;
                        if (address < start || address >= (ulong)Stm32Target.FlashBase + (uint)target.FlashBytes ||
                            !memory.TryAdd((uint)address, record[i + 4]))
                            throw new InvalidDataException("Intel HEX writes outside application flash or contains overlapping records.");
                    }
                    break;
                case 1 when record[0] == 0 && offset == 0: ended = true; break;
                case 2 when record[0] == 2 && offset == 0: upper = (uint)(record[4] << 8 | record[5]) << 4; break;
                case 4 when record[0] == 2 && offset == 0: upper = (uint)(record[4] << 8 | record[5]) << 16; break;
                case 3 or 5 when record[0] == 4 && offset == 0: break; // Startup vectors remain authoritative.
                default: throw new InvalidDataException("Unsupported or malformed Intel HEX record.");
            }
        }
        if (!ended || memory.Count == 0 || memory.First().Key != start)
            throw new InvalidDataException("Intel HEX requires EOF and application vectors at the expected flash address.");
        var data = Enumerable.Repeat((byte)255, checked((int)(memory.Last().Key - start + 1))).ToArray();
        foreach (var pair in memory) data[pair.Key - start] = pair.Value;
        return new(start, data);
    }

    private static Stm32Firmware ParseDfu(byte[] data)
    {
        // ST DfuSe: 11-byte prefix, 274-byte target header, one element,
        // and the mandatory 16-byte DFU suffix. Only internal flash alt 0.
        if (data.Length < 309 || Encoding.ASCII.GetString(data, 0, 5) != "DfuSe" || data[5] != 1 || data[10] != 1 ||
            (Read32(data, 6) != data.Length - 16 && Read32(data, 6) != data.Length) || Encoding.ASCII.GetString(data, 11, 6) != "Target" || data[17] != 0 ||
            Read32(data, 281) != 1 || Read32(data, 277) != data.Length - 301)
            throw new InvalidDataException("Unsupported DfuSe container: require version 1, one internal-flash target (alt 0) and one element.");
        var suffix = data.Length - 16;
        if (data[suffix + 8] != 'U' || data[suffix + 9] != 'F' || data[suffix + 10] != 'D' || data[suffix + 11] != 16 ||
            Read16(data, suffix + 6) != 0x011a || Crc(data.AsSpan(0, data.Length - 4)) != Read32(data, data.Length - 4))
            throw new InvalidDataException("DfuSe suffix or CRC is invalid.");
        if ((Read16(data, suffix + 4) != 0xffff && Read16(data, suffix + 4) != 0x0483) ||
            (Read16(data, suffix + 2) != 0xffff && Read16(data, suffix + 2) != 0xdf11))
            throw new InvalidDataException("DfuSe USB IDs do not match STM32 ROM DFU.");
        var length = Read32(data, 289);
        if (length != data.Length - 309) throw new InvalidDataException("DfuSe element size is inconsistent.");
        return new(Read32(data, 285), data.AsSpan(293, checked((int)length)).ToArray());
    }

    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    internal static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return crc;
    }
}
