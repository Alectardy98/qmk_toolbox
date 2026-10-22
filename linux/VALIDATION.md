# Linux parity and release validation

The original twelve-section prompt was unavailable. The following twelve areas
cover the stated requirements and the current official implementation.

Upstream audited: qmk/qmk_toolbox master
`cde76bfd99ce8cb107fbb0f182230eaba0e5206b` (2026-08-20), downloaded 2026-10-22.
Source comparison includes both `windows/QMK Toolbox` and `macos/QMK Toolbox`.
Official bootloader families are unchanged from the checked-in Windows enum;
upstream's Atmel DFU driver change concerns Windows WinUSB.

| Area | Implemented / evidence | Remaining release validation |
| --- | --- | --- |
| 1. Existing project and baseline | Existing Linux C#/Avalonia project retained; clean initial git status; original generated distributions preserved | Review the source diff against the user's working baseline |
| 2. USB discovery | Startup enumeration, all changes per poll, physical port identity, revision-sensitive QMK detection, corrected upstream IDs, SAM-BA entry, cancellable monitor | Real connect/disconnect/reconnect and simultaneous devices |
| 3. Bootloader families | APM32/Atmel/QMK/SAM-BA/AVRISP/BootloadHID/Caterina/GD32V/HalfKay/Kiibohd/LUFA/STM32/USBasp/USBtiny/WB32 backends retained; mapping regression checks | Flash/readback on representative hardware for every family; firmware compatibility |
| 4. Tool execution | Private 0700 extraction, no shared /tmp executables, stdout/stderr drain, five-minute timeout, nonzero exit stops multi-step flash | Native tools on clean target distributions; tool provenance/licensing audit beyond pre-existing binaries |
| 5. Serial and storage | Serial USB ancestry lookup replaces ttyACM0 assumption; LUFA volume matched through block-device USB ancestry and flushed | Real SAM-BA/AVRISP/Caterina enumeration timing and LUFA drive eject |
| 6. Flash/EEPROM/reset | Background operation with busy state, capability-aware controls, one-target guard, EEPROM uses bundled reset.eep | Hardware flash, EEPROM clear and reset; left/right EEPROM choices provided |
| 7. Automatic flashing | Opt-in auto flash on connection; existing file and MCU bounds checked; busy operations cannot overlap | Repeated hardware campaign, reconnect during an operation, firmware change workflow |
| 8. HID Listen | Integrated hidraw console, page/usage descriptor filtering, per-device streaming UTF-8 decoder, line buffering, reconnect, permission notice | Live console traffic, report-ID variants, separate console view; protocol reference verification unavailable |
| 9. Desktop UI | Responsive logical layout replaces stretched Viewbox; selectable bounded log, save/clear, file picker/drop, MCU persistence, focused key tester, About/upstream releases | Interactive accessibility, key tester keyboard diagram, translations, shortcuts, file association |
| 10. Wayland/HiDPI | Avalonia 11 and logical sizing; XWayland launch supported by selected backend | Native Wayland is not implemented; fractional/mixed-DPI visual testing remains |
| 11. Packaging | Self-contained x86-64 AppImage and Debian archive scripts; official udev rules/helper with source/license; no system installation during builds | Clean Debian install/uninstall, distro matrix, AppImage native dependencies, arm64, RPM/Flatpak if required |
| 12. Tests/release | 41 regression checks including headless UI layout, Release build, dependency vulnerability audit, GUI launch smoke, package archive checks | Hardware matrix, long-duration hotplug/console test, independent review, signed release and distribution |

## Evidence

Local generated evidence is under `linux/build/`: `build.log`, `tests.log`,
`dependency-audit.log`, `gui-smoke.log`, package build logs, and upstream source.
Run commands in `package/README.md` to reproduce; tests perform no USB writes.
The port is **not yet certified for full feature parity or production release**.
A build/launch pass does not establish hardware flash correctness.

The read-doc lookup `befafb8d4fae` reported the Calibre metadata database at
`/home/alec/Documents/calibre-library/metadata.db` unavailable. Descriptor tests
use synthetic fixtures and upstream's console usage tuple; hardware/protocol
semantics remain unverified against that library.

## STM32 implementation update

See [STM32-SUPPORT.md](STM32-SUPPORT.md) for new profile-guarded F103/F401/F411
support, firmware validation, DFU layout preflight, optional explicit UART, and
simulated lifecycle/Auto-Flash tests. The original 41 checks are retained and
extended by the STM32 suite. No physical device was flashed. These updates tighten
the previous generic STM32 DFU workflow to explicitly supported MCU profiles.
