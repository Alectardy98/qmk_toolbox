using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QMK_Toolbox.Stm32;

internal enum Stm32Protocol { RomDfu, Stm32Duino, RomUart }

internal sealed record Stm32Target(string Name, int FlashBytes, int RamBytes, bool IsF103)
{
    internal const uint FlashBase = 0x08000000;
    internal const uint RamBase = 0x20000000;
    internal static readonly Stm32Target[] All =
    {
        new("STM32F103C8 — Blue Pill (64 KB)", 64 * 1024, 20 * 1024, true),
        new("STM32F103CB — Blue Pill (128 KB)", 128 * 1024, 20 * 1024, true),
        new("STM32F401CC — Black Pill (256 KB)", 256 * 1024, 64 * 1024, false),
        new("STM32F411CE — Black Pill (512 KB)", 512 * 1024, 128 * 1024, false)
    };
    internal static Stm32Target Find(string name) => All.FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidDataException("Select and confirm the actual STM32 MCU in the separate STM32 target control.");

    internal uint ApplicationAddress(Stm32Protocol protocol)
    {
        if (protocol == Stm32Protocol.RomDfu && IsF103)
            throw new InvalidDataException("STM32F103C8/CB has no factory USB DFU path. Use an installed STM32duino bootloader or explicit ROM UART flashing.");
        if (protocol == Stm32Protocol.Stm32Duino && !IsF103)
            throw new InvalidDataException("The STM32duino backend here is for STM32F103 with the 8 KB bootloader, not F401/F411 ROM DFU.");
        return protocol == Stm32Protocol.Stm32Duino ? FlashBase + 0x2000 : FlashBase;
    }
}
