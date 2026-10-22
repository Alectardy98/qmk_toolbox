# QMK Toolbox Linux

Build with .NET SDK 10 (`global.json` allows current 10.0 feature bands).
The application targets Linux x86-64; bundled flash tools are x86-64 ELF files.

From `linux/`:

```sh
dotnet build QMKToolbox.sln -c Release
dotnet run --project tests/QMKToolbox.Tests.csproj -c Release
./package/build-appimage.sh
./package/build-deb.sh
```

AppImage requires `appimagetool`. Debian packaging requires Python 3 and uses
standard ar/tar.xz archives without requiring a Debian host. Generated releases,
staging trees, checksums, and local validation logs live in `linux/build/`.
The existing `linux/dist/`, `linux/AppDir/`, and root AppImage are preserved.

Run `build/QMKToolbox-x86_64.AppImage` or `build/publish/QMKToolbox`.
Firmware can be selected with Open, dropped onto the window, or supplied as the
first command-line argument. MCU and the last file are remembered. Auto flash
is deliberately off on launch. Connect one bootloader at a time; multiple
bootloaders disable flashing because several vendor tools cannot target a
specific physical device. Flashing and reset run in the background, and a
failed command stops the sequence. Tools time out after five minutes.

The integrated HID console scans hidraw interfaces and displays completed
UTF-8 lines with a device prefix. Only interfaces whose descriptor identifies
the PJRC console collection are opened; normal keyboard interfaces remain
with the kernel. The console checkbox controls display. The key tester only
receives keys while its window has focus; desktop shortcuts can be intercepted
by the compositor.

Avalonia uses logical layout dimensions for HiDPI. This build uses the X11
backend, including XWayland on Wayland desktops; native Wayland is not claimed.
Test mixed-DPI display movement and fractional scaling on each target desktop.

## Permissions

Do not run the application as root. The package includes official qmk_udev
v0.1.23 rules and the matching x86-64 `qmk_id` helper. For an AppImage, extract
its contents and run `install-udev-rules.sh` from the extracted directory. The
installer invokes sudo, installs the rules/helper, and reloads rules. Reconnect
the keyboard afterwards. The Debian package stages them in `/usr/lib/udev/`;
reload udev rules and reconnect after installation. Neither build script
installs anything on the host.

`qmk_id` is GPL-2.0-or-later; its complete upstream source archive and GPLv2
text accompany both package formats. Source and binary originate from:
https://github.com/qmk/qmk_udev/releases/tag/v0.1.23

## Runtime dependencies and limits

A self-contained .NET runtime is included. Native system libraries are still
needed: glibc >= 2.34, X11/ICE/SM, fontconfig, libusb 1.0 and 0.1, libftdi1,
hidapi-libusb, libelf, readline, ICU, OpenSSL, zlib, and the usual .NET native
runtime libraries. The Debian control file declares these dependencies.
AppImage is not a claim of compatibility with every Linux distribution.
See `VALIDATION.md` for the acceptance matrix and remaining release checks.

## GNOME desktop integration for local builds

`./package/build-appimage.sh` automatically refreshes the user launcher after
success. It always installs **QMKToolbox.desktop**, with `StartupWMClass=QMKToolbox`,
`Icon=qmk-toolbox`, and an absolute Exec pointing to this checkout's stable
`linux/build/QMKToolbox-x86_64.AppImage`. The application sets the same XWayland
window class explicitly. The image is replaced atomically, so a running copy
can remain open during rebuilding. No versioned or temporary mount path is
stored in the launcher. The generated AppDir and Debian package use that same
desktop filename and icon name.

To refresh manually, or pin it to the GNOME dock:

```sh
python3 package/install-desktop.py
python3 package/install-desktop.py --pin
```

The installer reuses the repository's existing QMK icon, updates desktop/icon
caches, migrates existing QMK favorites, and masks duplicate registrations with
`Hidden=true` user overrides. Only launchers named exactly “QMK Toolbox” and
the known `qmk-toolbox.desktop` alias are migrated; unrelated keyboard tools
are preserved. System desktop files and old AppImage binaries are not removed.
Previous changed user launchers are backed up under
`${XDG_DATA_HOME:-~/.local/share}/qmk-toolbox/desktop-backups/`.

For CI or packaging builds that should not register anything on the desktop,
set `QMK_TOOLBOX_SKIP_DESKTOP_INTEGRATION=1`. The integration installer requires
Python 3; cache tools and GNOME gsettings are used when available. Tests for
migration, system overrides, idempotence, and missing builds run with:

```sh
python3 -m unittest discover -s tests -p 'test_desktop_integration.py'
```

GNOME monitors these user directories; a shell restart is normally unnecessary.
The files and dock favorite persist across login sessions. On Wayland this
Avalonia version uses XWayland, with window association through WM_CLASS.

## STM32 Blue Pill and Black Pill

See [STM32-SUPPORT.md](../STM32-SUPPORT.md) for the per-MCU compatibility matrix,
bootloader prerequisites, accepted formats and safe test procedures. STM32 USB
flashing now requires the separate STM32 target profile; the AVR dropdown is
unchanged. ROM DFU and STM32duino have different layouts and are not interchangeable.
UART is an explicit action requiring optional `stm32flash`; it is never auto-probed
or auto-flashed. All STM32 hardware acceptance remains pending physical testing.
