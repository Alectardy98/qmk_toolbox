"""Exercise desktop migration in isolated XDG directories, never the real desktop."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

PACKAGE = Path(__file__).resolve().parents[1] / 'package'
spec = importlib.util.spec_from_file_location('install_desktop', PACKAGE / 'install-desktop.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class DesktopIntegrationTests(unittest.TestCase):
    def test_migrate_mask_and_repeat(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            user, system = root / 'user', root / 'system'
            for directory in (user, system):
                (directory / 'applications').mkdir(parents=True)
            canonical = user / 'applications/QMKToolbox.desktop'
            canonical.write_text('[Desktop Entry]\nName=QMK Toolbox\nExec=/old/AppImage\n')
            old = user / 'applications/qmk-toolbox.desktop'
            old.write_text('[Desktop Entry]\nName=QMK Toolbox\nIcon=/missing.png\n')
            managed = system / 'applications/appimagekit-old.desktop'
            managed.write_text('[Desktop Entry]\nName=QMK Toolbox\nExec=/system/old.AppImage\n')
            unrelated = user / 'applications/keyboard-util.desktop'
            unrelated.write_text('[Desktop Entry]\nName=Keyboard Utility\nExec=/repos/qmk/util\n')
            original_managed, original_unrelated = managed.read_bytes(), unrelated.read_bytes()
            image = root / 'latest.AppImage'
            image.write_bytes(b'new build')
            image.chmod(0o755)
            installer.install(image, user, [system], refresh=False)
            self.assertEqual(installer.desktop_fields(canonical)['StartupWMClass'], 'QMKToolbox')
            self.assertEqual(installer.desktop_fields(old)['Hidden'], 'true')
            self.assertEqual(installer.desktop_fields(user / 'applications/appimagekit-old.desktop')['Hidden'], 'true')
            self.assertEqual(managed.read_bytes(), original_managed)
            self.assertEqual(unrelated.read_bytes(), original_unrelated)
            self.assertEqual((user / 'icons/hicolor/256x256/apps/qmk-toolbox.png').read_bytes(),
                             (PACKAGE / 'qmk-toolbox.png').read_bytes())
            backup_files = sorted((user / 'qmk-toolbox/desktop-backups').rglob('*'))
            installed, modified = canonical.read_bytes(), canonical.stat().st_mtime_ns
            image.write_bytes(b'rebuilt version')
            installer.install(image, user, [system], refresh=False)
            self.assertEqual(canonical.read_bytes(), installed)
            self.assertEqual(canonical.stat().st_mtime_ns, modified)
            self.assertEqual(sorted((user / 'qmk-toolbox/desktop-backups').rglob('*')), backup_files)

    def test_missing_image_does_not_modify_registration(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(SystemExit):
                installer.install(root / 'absent.AppImage', root / 'user', [], refresh=False)
            self.assertFalse((root / 'user').exists())


if __name__ == '__main__':
    unittest.main()
