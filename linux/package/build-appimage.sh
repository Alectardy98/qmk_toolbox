#!/bin/sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"
command -v appimagetool >/dev/null
mkdir -p build
rm -rf build/publish build/AppDir
dotnet publish QMKToolbox/QMKToolbox.csproj -c Release -r linux-x64 --self-contained true -o build/publish
mkdir -p build/AppDir/usr/bin build/AppDir/usr/share/applications build/AppDir/usr/share/icons/hicolor/256x256/apps
cp -a build/publish/. build/AppDir/usr/bin/
cp package/QMKToolbox.desktop build/AppDir/usr/share/applications/
cp package/QMKToolbox.desktop build/AppDir/
cp package/qmk-toolbox.png build/AppDir/
cp package/qmk-toolbox.png build/AppDir/usr/share/icons/hicolor/256x256/apps/
cp package/50-qmk.rules package/install-udev-rules.sh package/qmk_id package/qmk_udev.COPYING package/qmk_udev-v0.1.23-source.tar.gz package/THIRD-PARTY.md build/AppDir/
cat > build/AppDir/AppRun <<'RUN'
#!/bin/sh
HERE="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec "$HERE/usr/bin/QMKToolbox" "$@"
RUN
chmod +x build/AppDir/AppRun
# Replace atomically: an already running AppImage keeps its mounted inode,
# while future launches resolve the same pathname to the new build.
ARCH=x86_64 appimagetool build/AppDir build/QMKToolbox-x86_64.AppImage.new
mv -f build/QMKToolbox-x86_64.AppImage.new build/QMKToolbox-x86_64.AppImage
sha256sum build/QMKToolbox-x86_64.AppImage > build/QMKToolbox-x86_64.AppImage.sha256

# Refresh the stable local launcher after a successful build. Packaging-only
# environments can opt out; no system files are changed.
if [ "${QMK_TOOLBOX_SKIP_DESKTOP_INTEGRATION:-0}" != "1" ]; then
    python3 package/install-desktop.py
fi
