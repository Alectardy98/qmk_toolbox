# QMK Toolbox for Linux

## Build

Requirements:

- .NET SDK 6
- appimagetool
- Python 3
- Pillow

Run `./package/build-appimage.sh` from the `linux` directory.

The resulting image is `QMKToolbox-x86_64.AppImage`.

## Device permissions

QMK bootloaders may require udev rules for non-root access.

If your distribution already packages QMK's 50-qmk.rules, no additional setup is required.

Otherwise, place 50-qmk.rules next to install-udev-rules.sh and run `./install-udev-rules.sh`.

Reconnect or reset the keyboard after installing the rules.
