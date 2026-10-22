# Blue Pill and Black Pill support on Linux

This implementation distinguishes MCU, board wiring, and bootloader protocol.
A board's colour/name does not select a protocol. USB IDs identify a bootloader,
not a trustworthy MCU model. No physical devices were flashed during development.

## Compatibility matrix

| Board / actual MCU | Factory ROM USB DFU | Installed STM32duino USB DFU | Factory ROM UART |
| --- | --- | --- | --- |
| Blue Pill STM32F103C8T6 (64 KB flash, 20 KB RAM) | Not a supported ROM interface | Supported with the 8 KB bootloader, alt 2, application at `0x08002000` | Supported via optional `stm32flash`, manual port, application at `0x08000000` |
| Blue Pill STM32F103CBT6 (128 KB flash, 20 KB RAM) | Not a supported ROM interface | Same protocol/offset; separate 128 KB profile | Same manual UART path; separate 128 KB profile |
| Black Pill STM32F401CCU6 (256 KB flash, 64 KB RAM) | Supported: `0483:df11`, internal flash alt 0, application at `0x08000000` | F103 STM32duino backend is not applicable | Supported via optional `stm32flash`; preflight chip ID `0x423` |
| Black Pill STM32F411CEU6 (512 KB flash, 128 KB RAM) | Supported: `0483:df11`, internal flash alt 0, application at `0x08000000` | F103 STM32duino backend is not applicable | Supported via optional `stm32flash`; preflight chip ID `0x431` |

“Supported” means implemented and regression-tested with simulated devices.
Electrical operation, ROM revisions, timing, board oscillator requirements,
and actual flash/readback/boot remain to be verified on physical hardware.
C8 devices are restricted to the rated 64 KB; no undocumented extra flash is assumed.

## Existing capabilities and additions

Previously the application detected `0483:df11` and `1eaf:0003`, downloaded raw
`.bin` files through `dfu-util`, and reused the general hotplug/Auto-Flash logic.
STM32duino already used alt 2. UART was absent.

Added:

- A separate **STM32 target** selector for the four MCU variants; the AVR MCU
  list is unchanged. The STM32 selection is not persisted, requiring a fresh
  choice on application startup. A matching target/file is required for Auto-Flash.
- USB DFU-interface checks, actual product/manufacturer/serial logging, physical
  port targeting, and USB-address tracking to catch same-port re-enumeration.
- A read-only `dfu-util -l` preflight before every USB download. ROM DFU requires
  an internal-flash layout whose capacity matches the selected profile;
  STM32duino requires alt 2 advertising `0x08002000`. Unrecognized/protected
  layouts are rejected. This describes memory compatibility, not authentication.
- Input validation and an immutable private binary snapshot before transfer.
- Explicit ROM UART flashing with chip-ID preflight, write verification (`-v`),
  manual port selection, and normal tool output/error capture. A mismatched
  family ID stops the write; F103 C8/CB still require the correct user selection.
- Process launch/dependency/permission diagnostics, temporary-file cleanup,
  and simulated regression tests covering failures and lifecycle behavior.

## Formats and compatibility checks

| Method | Accepted inputs | Processing |
| --- | --- | --- |
| F401/F411 ROM USB DFU | `.bin`, Intel `.hex`, DfuSe `.dfu` | Normalize to binary; download with explicit application address and leave |
| F103 STM32duino | `.bin`, Intel `.hex` | Normalize to binary; generic DFU alt 2 and USB reset; never send DfuSe address commands |
| ROM UART | `.bin`, Intel `.hex` | Normalize to binary; `stm32flash` probe followed by bounded write/verify/go |

HEX must have valid lengths, checksums, EOF, non-overlapping records, and all
addresses inside the selected application region. DfuSe must have a valid CRC,
matching or wildcard VID/PID, one internal-flash target at alt 0, and one element.
Multi-target/multi-element files, option bytes, RAM targets, arbitrary relocation,
bootloader installation images, and non-DfuSe files named `.dfu` are rejected.

All application images must start at the expected bootloader address, fit the
selected flash region, and contain plausible RAM/Thumb reset vectors. Raw `.bin`
files have no MCU/keyboard identity metadata: these checks cannot establish that
a binary was built for the correct QMK keyboard, matrix pins, or exact MCU.
Select the actual chip and use a firmware build configured for that board and
bootloader. Do not infer F401 versus F411 from `0483:df11`.

## Prerequisites

- A supported MCU and bootloader, correctly entered into bootloader mode.
  Use the board maker's boot/reset instructions and schematic, not colour alone.
- A data-capable USB cable for USB DFU, working native `libusb` dependencies,
  and QMK udev permissions. The bundled rules include both USB IDs; reconnect
  after rules installation. No Windows driver installation is required on Linux.
- STM32duino must already be installed on an F103 board and provide the alt-2
  8 KB application layout. Installing a bootloader initially requires a separate,
  approved SWD/UART procedure; this application never installs one automatically.
- UART requires `stm32flash` installed in an absolute PATH directory, an accessible
  serial device, appropriate adapter voltage/wiring, and ROM boot mode. Consult
  ST AN2606 and the board schematic for the applicable USART and boot selection.
  On Manjaro use the distribution's package management to install the optional
  utility; the application neither installs packages nor invokes sudo.
- UART is never inferred from a USB-adapter VID/PID, probed in the background,
  or used by Auto-Flash. Its explicit button is disabled while a recognized USB
  bootloader is present. Select the port deliberately, preferably by stable
  `/dev/serial/by-id/` name. Adapter-driven reset/boot switching is not automated.

## Safe test procedure (no flashing until separately authorized)

1. Keep **Auto flash off**. Check the MCU marking and obtain a board-specific QMK
   build with the correct bootloader/link address. Back up existing firmware
   separately if needed before later authorizing any programming operation.
2. **F401CC/F411CE Black Pill USB:** follow the board instructions to enter ROM
   boot mode. Expect `0483:df11` and a “STM32 DfuSe USB” connection log, not automatic
   identification of the exact chip. Select its STM32 profile and firmware.
3. **F103C8/CB Blue Pill USB:** confirm the 8 KB STM32duino bootloader is installed.
   Enter that bootloader as its documentation specifies. Expect `1eaf:0003` and a
   “STM32duino USB” log. Choose the actual C8/CB profile and firmware linked at
   `0x08002000`. Factory ROM mode alone will not create this USB bootloader.
4. **Any listed variant over UART:** verify the board-specific wiring and ROM
   boot selection before connecting an adapter. Enter its serial path and actual
   target profile. No port is opened merely by entering the path or selecting
   firmware. The explicit **Flash STM32 UART** action begins probe and write.
5. Check the validation/status text. Disconnect/reconnect USB and verify the
   connection log and flash-button state. These checks perform no download while
   Auto-Flash remains off. Invalid images and unconfirmed profiles stay blocked.
6. **Only after explicit authorization to flash that board**, click Flash (USB)
   or Flash STM32 UART. USB first checks the reported layout; UART first checks
   the reported chip ID. Monitor tool progress/errors. UART requests readback
   verification; USB transfer success alone is not independent readback proof.
7. Restore the documented normal boot selection and reset if necessary. Confirm
   the intended keyboard firmware enumerates and behaves correctly. Only after
   that manual test should Auto-Flash be tested on repeated USB bootloader entries.

ROM UART writes at `0x08000000`, so it replaces any flash-resident bootloader
covered by the application image. It is not the route for preserving STM32duino
while loading an application linked at `0x08002000`; use the USB STM32duino method
for that workflow.

## Other bootloaders and limits

- Legacy Maple alt-1 `0x08005000`, HID bootloaders, UF2 mass-storage bootloaders,
  custom DfuSe bootloaders in flash, and clone MCU protocols are not silently
  treated as either supported USB method. They require distinct validated profiles.
- The guarded STM32 profile set currently covers the four MCU variants above.
  Other devices using ST's shared `0483:df11` still enumerate, but cannot be safely
  programmed using these profiles unless their actual MCU is explicitly supported.
  This tightens the previous unrestricted generic-DFU behavior intentionally.
- Different MCUs with identical USB IDs or identical memory capacities cannot be
  conclusively identified through these descriptors. Actual firmware correctness
  and chip selection remain user responsibilities. No protection/unprotect,
  mass erase, fuse, or option-byte commands are provided.
- Polling checks every 500 ms. USB address changes detect fast re-enumeration;
  transfers stay serialized while the old operation completes or fails. Transient
  disconnects can still require manual reset/retry. Tool timeout remains five minutes.
- STM32 ROM USB reset/leave behavior and board clock constraints vary by ROM
  revision. Check AN2606's applicable limitations and the device errata before
  hardware acceptance; bootability has not been physically verified here.

## References and verification status

Primary references consulted:

- [ST AN2606 Rev 70](https://www.st.com/content/ccc/resource/technical/document/application_note/b9/9b/16/3a/12/1e/40/0c/CD00167594.pdf/files/CD00167594.pdf/_jcr_content/translations/en.CD00167594.pdf),
  F1 overview p.40; F401xB(C) section 35 pp.170–174; F411 section 38 pp.185–189.
- [ST F103C8](https://www.st.com/en/microcontrollers-microprocessors/stm32f103c8.html),
  [F401CC](https://www.st.com/en/microcontrollers-microprocessors/stm32f401cc.html),
  [F411CE](https://www.st.com/en/microcontrollers-microprocessors/stm32f411ce.html).
- [dfu-util manual](https://dfu-util.sourceforge.net/dfu-util.1.html) and
  [DfuSe notes](https://dfu-util.sourceforge.net/dfuse.html).
  The repository's bundled binary identifies itself as dfu-util 0.11; its official
  source release was inspected for listing syntax and DfuSe container handling.
- [STM32duino config](https://github.com/rogerclarkmelbourne/STM32duino-bootloader/blob/master/config.h)
  and USB descriptors: bootloader ID and alt-1/alt-2 application offsets.
- [stm32flash manual](https://github.com/stm32duino/stm32flash/blob/main/stm32flash.1),
  main.c and dev_table.c: UART options, chip-ID output and supported family IDs.

The read-doc lookup `7f9441f842db` reported
`/home/alec/Documents/calibre-library/metadata.db` unavailable. Formal library
verification of base documents and per-device errata is therefore **unavailable**;
physical hardware acceptance remains pending. Online references informed the
implementation; regression results are software evidence, not hardware certification.

Run `dotnet run --project linux/tests/QMKToolbox.Tests.csproj -c Release` from the
repository root. Tests use synthetic USB snapshots, fake UART chip IDs, and
in-memory/simulated transfer results; no connected device is opened or flashed.
Generated build/test evidence is under `linux/build/stm32/`.
