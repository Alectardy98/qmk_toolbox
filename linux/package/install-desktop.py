#!/usr/bin/env python3
"""Install the local AppImage's stable desktop identity, without system writes."""
import argparse
import configparser
from datetime import datetime, timezone
import os
from pathlib import Path
import shutil
import subprocess

APP_ID = 'QMKToolbox'
DESKTOP_ID = APP_ID + '.desktop'
PACKAGE = Path(__file__).resolve().parent


def desktop_fields(path):
    config = configparser.ConfigParser(interpolation=None, strict=False)
    try:
        config.read_string(path.read_text())
        return config['Desktop Entry']
    except (OSError, UnicodeError, configparser.Error, KeyError):
        return {}


def exec_quote(path):
    # Exec has desktop-entry escaping in addition to argument quoting.
    value = str(path).replace('%', '%%')
    for character in ('\\', '"', '`', '$'):
        value = value.replace(character, '\\' + character)
    return '"' + value.replace('\\', '\\\\') + '"'


def install(appimage, data_home, data_dirs, refresh=True, pin=False):
    appimage = appimage.absolute()  # Do not follow a versioned symlink target.
    if not appimage.is_file() or not os.access(appimage, os.X_OK):
        raise SystemExit(f'Build the executable AppImage first: {appimage}')
    if any(c in str(appimage) for c in '\n\r'):
        raise SystemExit('The AppImage path must not contain a newline.')
    applications = data_home / 'applications'
    icon = data_home / 'icons/hicolor/256x256/apps/qmk-toolbox.png'
    backup = data_home / 'qmk-toolbox/desktop-backups' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    applications.mkdir(parents=True, exist_ok=True)
    icon.parent.mkdir(parents=True, exist_ok=True)
    changed = False

    def write(path, text):
        nonlocal changed
        if path.exists() and path.read_text() == text:
            return
        if path.exists() or path.is_symlink():
            backup.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, backup / path.name, follow_symlinks=True)
        temporary = path.with_name(path.name + '.tmp')
        temporary.write_text(text)
        temporary.chmod(0o644)
        temporary.replace(path)
        changed = True

    # Discover by application name, never by the executable containing "qmk":
    # unrelated keyboard utilities often reference qmk source directories.
    duplicates = set()
    for root in [data_home, *data_dirs]:
        for path in sorted((root / 'applications').rglob('*.desktop')):
            desktop_id = str(path.relative_to(root / 'applications')).replace('/', '-')
            if desktop_id == DESKTOP_ID:
                continue
            fields = desktop_fields(path)
            name = fields.get('Name', '').strip().casefold()
            if name == 'qmk toolbox' or desktop_id == 'qmk-toolbox.desktop':
                duplicates.add(desktop_id)
    for desktop_id in sorted(duplicates):
        # Same-name user overrides also mask system/AppImage registrations.
        write(applications / desktop_id,
              '[Desktop Entry]\nType=Application\nName=QMK Toolbox\nHidden=true\n')
        print(f'Disabled duplicate: {desktop_id} (user override)')

    source_icon = PACKAGE / 'qmk-toolbox.png'
    if not icon.exists() or icon.read_bytes() != source_icon.read_bytes():
        if icon.exists():
            backup.mkdir(parents=True, exist_ok=True)
            shutil.copy2(icon, backup / 'qmk-toolbox.png')
        shutil.copyfile(source_icon, icon)
        icon.chmod(0o644)
        changed = True
    template = (PACKAGE / DESKTOP_ID).read_text()
    launcher = template.replace('Exec=QMKToolbox %f', 'Exec=' + exec_quote(appimage) + ' %f')
    write(applications / DESKTOP_ID, launcher)

    if refresh:
        for command in (['update-desktop-database', str(applications)],
                        ['gtk-update-icon-cache', '-f', '-t', str(data_home / 'icons/hicolor')]):
            if shutil.which(command[0]):
                subprocess.run(command, check=True)
        if shutil.which('gsettings'):
            # Migrate existing pins without disturbing the order of other apps.
            import ast
            result = subprocess.run(['gsettings', 'get', 'org.gnome.shell', 'favorite-apps'],
                                    text=True, capture_output=True)
            if result.returncode == 0:
                favorites = ast.literal_eval(result.stdout.removeprefix('@as ').strip())
                updated = []
                for item in favorites:
                    item = DESKTOP_ID if item in duplicates else item
                    if item != DESKTOP_ID or item not in updated:
                        updated.append(item)
                if pin and DESKTOP_ID not in updated:
                    updated.append(DESKTOP_ID)
                if updated != favorites:
                    subprocess.run(['gsettings', 'set', 'org.gnome.shell', 'favorite-apps', repr(updated)], check=True)
    print(f'Launcher: {applications / DESKTOP_ID}')
    print(f'AppImage: {appimage}')
    print(f'Icon: {icon}')
    if backup.exists():
        print(f'Previous files backed up to: {backup}')
    print('Integration updated.' if changed else 'Integration already current.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--appimage', type=Path, default=PACKAGE.parent / 'build/QMKToolbox-x86_64.AppImage')
    parser.add_argument('--data-home', type=Path, default=Path(os.environ.get('XDG_DATA_HOME', str(Path.home() / '.local/share'))))
    parser.add_argument('--data-dirs', default=os.environ.get('XDG_DATA_DIRS', '/usr/local/share:/usr/share'))
    parser.add_argument('--no-refresh', action='store_true', help='Skip desktop caches and GNOME favorites (for isolated tests).')
    parser.add_argument('--pin', action='store_true', help='Also pin the stable launcher to the GNOME dock.')
    args = parser.parse_args()
    install(args.appimage, args.data_home, [Path(p) for p in args.data_dirs.split(':') if p],
            refresh=not args.no_refresh, pin=args.pin)


if __name__ == '__main__':
    main()
