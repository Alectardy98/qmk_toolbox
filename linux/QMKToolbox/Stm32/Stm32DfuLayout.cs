using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QMK_Toolbox.Stm32;

internal static class Stm32DfuLayout
{
    internal static void Validate(string report, string path, Stm32Target target, Stm32Protocol protocol)
    {
        var id = protocol == Stm32Protocol.RomDfu ? "0483:df11" : "1eaf:0003";
        var alt = protocol == Stm32Protocol.RomDfu ? 0 : 2;
        var lines = report.Split('\n').Where(line => line.Contains("Found DFU:", StringComparison.Ordinal) &&
            line.Contains($"[{id}]", StringComparison.OrdinalIgnoreCase) &&
            line.Contains($"path=\"{path}\"", StringComparison.Ordinal) &&
            Regex.IsMatch(line, $@"\balt={alt},")).ToArray();
        if (lines.Length != 1)
            throw new IOException("Cannot identify exactly one matching DFU-mode interface at the selected USB port. Check boot mode, permissions and reconnect.");
        var name = Regex.Match(lines[0], "name=\"([^\"]+)\"").Groups[1].Value.Trim();
        if (protocol == Stm32Protocol.Stm32Duino)
        {
            if (!Regex.IsMatch(name, @"\b0x0?8002000\b", RegexOptions.IgnoreCase))
                throw new IOException("STM32duino alt 2 does not advertise the expected 0x08002000 application offset. Legacy or custom bootloaders require their own profile.");
            return;
        }
        var map = Regex.Match(name, @"^@Internal Flash\s*/0x08000000/(.+)$", RegexOptions.IgnoreCase);
        if (!map.Success) throw new IOException("The DFU device does not advertise a recognized internal flash memory map.");
        ulong capacity = 0;
        foreach (var entry in map.Groups[1].Value.Split(','))
        {
            var sector = Regex.Match(entry.Trim(), @"^(\d+)\*(\d+)([BKM])g$", RegexOptions.IgnoreCase);
            if (!sector.Success) throw new IOException("Unrecognized or protected DFU flash sectors; refusing automatic programming.");
            var unit = sector.Groups[3].Value.ToUpperInvariant() switch { "K" => 1024ul, "M" => 1024ul * 1024, _ => 1ul };
            if (!uint.TryParse(sector.Groups[1].Value, out var count) || !uint.TryParse(sector.Groups[2].Value, out var size) || count == 0 || count > 4096 || size == 0 || size > 4 * 1024 * 1024)
                throw new IOException("DFU memory map contains invalid sector sizes.");
            capacity += (ulong)count * size * unit;
            if (capacity > 4 * 1024 * 1024) throw new IOException("DFU flash memory map is outside supported profiles.");
        }
        if (capacity != (uint)target.FlashBytes)
            throw new IOException($"Device reports {capacity / 1024} KB flash, incompatible with selected {target.Name}. No write was started.");
    }
}
