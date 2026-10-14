"""Regression checks for nested bundle corruption and publication allowlists."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from librarian_bundle import BUNDLE_FILES, GAME_TARGETS, validate_bundle


class BundleTests(unittest.TestCase):
    def setUp(self):
        cache = ROOT / '.research/unified-v1.3.0/tests'
        cache.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=cache)
        self.directory = Path(self.temp.name)
        for name in BUNDLE_FILES:
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
        self.mod = dict(id='Librarian', version='1.3.0', min_game_version='0.107.1', has_pck=True,
                        has_dll=True, dependencies=[dict(id='STS2-RitsuLib', min_version='0.6.4')])
        self.variants = dict(schema_version=1, mod_id='Librarian', mod_version='1.3.0', variants=[
            dict(game_version=v, assembly='Librarian', path=f'compat/{v}/Librarian.dll',
                 sha256=hashlib.sha256((self.directory / f'compat/{v}/Librarian.dll').read_bytes()).hexdigest())
            for v in GAME_TARGETS])
        self.write_manifests()

    def tearDown(self):
        self.temp.cleanup()

    def write_manifests(self):
        (self.directory / 'Librarian.json').write_text(json.dumps(self.mod), encoding='utf-8')
        (self.directory / 'librarian-variants.manifest').write_text(json.dumps(self.variants), encoding='utf-8')

    def test_full_bundle_and_record(self):
        identity = validate_bundle(self.directory)
        self.assertEqual(6, len(identity))
        self.assertEqual(identity, validate_bundle(self.directory, identity))

    def test_missing_or_extra_nested_file(self):
        extra = self.directory / 'compat/0.107.1/sts2.dll'
        extra.write_bytes(b'private dependency must never be distributed')
        with self.assertRaises(ValueError): validate_bundle(self.directory)
        extra.unlink()
        (self.directory / 'compat/0.111.0/Librarian.dll').unlink()
        with self.assertRaises(ValueError): validate_bundle(self.directory)

    def test_content_corruption(self):
        (self.directory / 'compat/0.107.1/Librarian.dll').write_bytes(b'corrupt')
        with self.assertRaises(ValueError): validate_bundle(self.directory)

    def test_unsafe_and_wrong_target_paths(self):
        for path in ('../Librarian.dll', '/Librarian.dll', 'compat/0.111.0/Librarian.dll'):
            self.variants['variants'][0]['path'] = path
            self.write_manifests()
            with self.assertRaises(ValueError): validate_bundle(self.directory)

    def test_version_dependency_and_duplicate_target(self):
        self.variants['mod_version'] = '1.2.0-beta13'
        self.write_manifests()
        with self.assertRaises(ValueError): validate_bundle(self.directory)
        self.variants['mod_version'] = '1.3.0'
        self.mod['dependencies'].append(dict(id='BaseLib', min_version='3.4.5'))
        self.write_manifests()
        with self.assertRaises(ValueError): validate_bundle(self.directory)
        self.mod['dependencies'].pop()
        self.variants['variants'][1] = self.variants['variants'][0].copy()
        self.write_manifests()
        with self.assertRaises(ValueError): validate_bundle(self.directory)

    def test_recorded_shared_pck_drift(self):
        identities = validate_bundle(self.directory)
        (self.directory / 'Librarian.pck').write_bytes(b'changed shared assets')
        with self.assertRaises(ValueError): validate_bundle(self.directory, identities)


if __name__ == '__main__':
    unittest.main()
