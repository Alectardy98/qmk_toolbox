#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK is required to build QMK Toolbox."
    exit 1
fi

if ! command -v appimagetool >/dev/null 2>&1; then
    echo "appimagetool is required to build the AppImage."
    exit 1
fi

rm -rf dist AppDir
dotnet publish QMKToolbox/QMKToolbox.csproj -c Release -r linux-x64 --self-contained true -o dist

mkdir -p AppDir/usr/bin AppDir/usr/share/applications AppDir/usr/share/icons/hicolor/256x256/apps
cp -a dist/. AppDir/usr/bin/
cp package/qmk-toolbox.desktop AppDir/usr/share/applications/qmk-toolbox.desktop
cp package/qmk-toolbox.desktop AppDir/qmk-toolbox.desktop

python -c 'from PIL import Image; img=Image.open("QMKToolbox/Resources/qmk.ico"); img.seek(getattr(img,"n_frames",1)-1); img=img.convert("RGBA"); img.thumbnail((256,256)); img.save("AppDir/qmk-toolbox.png"); img.save("AppDir/usr/share/icons/hicolor/256x256/apps/qmk-toolbox.png")'

printf "%s\n" "#!/bin/sh" 'HERE="$(dirname "$(readlink -f "$0")")"' 'exec "$HERE/usr/bin/QMKToolbox" "$@"' > AppDir/AppRun
chmod +x AppDir/AppRun

rm -f QMKToolbox-x86_64.AppImage
ARCH=x86_64 appimagetool AppDir QMKToolbox-x86_64.AppImage

echo "Built:"
ls -lh QMKToolbox-x86_64.AppImage
