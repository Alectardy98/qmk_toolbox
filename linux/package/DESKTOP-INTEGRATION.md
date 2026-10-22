# GNOME local integration — validated 2026-10-22

Retained desktop identity: `QMKToolbox.desktop`.
User launcher: `/home/alec/.local/share/applications/QMKToolbox.desktop`.
Exec: `/home/alec/Documents/GitHub/qmk_toolbox/linux/build/QMKToolbox-x86_64.AppImage`.
Icon: `qmk-toolbox`, resolving to the existing user hicolor 256px icon. Its
SHA256 equals the repository's `package/qmk-toolbox.png`; no replacement artwork
was introduced. Terminal=false. StartupWMClass and XWayland WM_CLASS both equal
`QMKToolbox`.

Initial duplicate registrations:

| Desktop file | Previous executable | Action |
| --- | --- | --- |
| `~/.local/share/applications/QMKToolbox.desktop` | `~/.local/bin/QMKToolbox.AppImage` (older installation) | Retained ID; Exec updated to current repository build |
| `~/.local/share/applications/qmk-toolbox.desktop` | Current repository AppImage, with nonexistent `Assets/qmk_icon.png` | Disabled with `Hidden=true` |

Original user files are backed up in
`~/.local/share/qmk-toolbox/desktop-backups/20261022T070732.813646Z/`.
The old AppImage binary remains untouched. No QMK Toolbox registration was found
in the system application directories or Flatpak exports. Other keyboard utility
launchers reference qmk source directories but are unrelated and were preserved.
No system files or packages were modified.

Validation performed:

- desktop-file-validate passes for the installed launcher, override, and template.
- Gio's application registry reports one visible QMK Toolbox ID.
- GTK resolves the existing QMK icon to a real file with the expected hash.
- gtk-launch QMKToolbox starts the configured AppImage without a terminal.
- /proc environment confirms APPIMAGE is the stable repository path and
  GIO_LAUNCHED_DESKTOP_FILE is the retained launcher.
- xprop confirms both WM_CLASS strings are QMKToolbox.
- The running mounted DLL matches the latest published DLL.
- An AppImage rebuild while running succeeds using atomic replacement, preserves
  the existing window, and leaves the launcher unchanged.
- A subsequent launcher activation starts the rebuilt AppImage successfully.
- GNOME favorite-apps contains QMKToolbox.desktop exactly once and no old alias.
- Isolated migration tests pass for user duplicates, system overrides, preservation
  of unrelated launchers, idempotence, and missing build behavior.

Direct GNOME Shell window/app introspection is denied by this session's security
policy. Dock association is verified through the matching desktop ID, favorite,
and live WM_CLASS; visual dock grouping was not directly inspected. GNOME was
not restarted and the user was not logged out. The desktop files, icon, and
gsettings favorite are persistent; a fresh-session visual check remains unperformed.

Generated evidence: `linux/build/desktop-integration/validation.log` and
`validation.json`, plus rebuild and isolated test logs in the same directory.
See `README.md` for how future builds refresh the same dock identity.
