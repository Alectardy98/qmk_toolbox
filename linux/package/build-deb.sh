#!/bin/sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"
VERSION="${VERSION:-0.3.4~linux1}"
STAGE="$ROOT/build/debian"
rm -rf "$STAGE"
dotnet publish QMKToolbox/QMKToolbox.csproj -c Release -r linux-x64 --self-contained true -o "$STAGE/opt/qmk-toolbox"
mkdir -p "$STAGE/DEBIAN" "$STAGE/usr/bin" "$STAGE/usr/share/applications" "$STAGE/usr/share/icons/hicolor/256x256/apps" "$STAGE/usr/lib/udev/rules.d"
cat > "$STAGE/DEBIAN/control" <<CONTROL
Package: qmk-toolbox
Version: $VERSION
Section: devel
Priority: optional
Architecture: amd64
Maintainer: QMK Toolbox Linux contributors
Depends: libc6 (>= 2.34), libx11-6, libice6, libsm6, libfontconfig1, libusb-1.0-0, libusb-0.1-4, libftdi1-2, libhidapi-libusb0, libelf1 | libelf1t64, libreadline8 | libreadline8t64, libgcc-s1, libstdc++6, zlib1g, libssl3 | libssl3t64, libicu76 | libicu74 | libicu72 | libicu70, libxrandr2, libxi6, libxcursor1
Description: QMK keyboard firmware flashing and HID console utility
CONTROL
ln -s /opt/qmk-toolbox/QMKToolbox "$STAGE/usr/bin/QMKToolbox"
cp package/QMKToolbox.desktop "$STAGE/usr/share/applications/"
cp package/qmk-toolbox.png "$STAGE/usr/share/icons/hicolor/256x256/apps/"
mkdir -p "$STAGE/usr/share/doc/qmk-toolbox"
install -m 0755 package/qmk_id "$STAGE/usr/lib/udev/qmk_id"
cp package/THIRD-PARTY.md package/qmk_udev.COPYING package/qmk_udev-v0.1.23-source.tar.gz "$STAGE/usr/share/doc/qmk-toolbox/"
cp package/50-qmk.rules "$STAGE/usr/lib/udev/rules.d/"
python3 package/make-deb.py "$STAGE" "build/qmk-toolbox_${VERSION}_amd64.deb"
