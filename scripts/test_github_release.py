"""Verify release preparation rejects mixed, unapproved or extra runtime files."""
import json
from pathlib import Path
import tempfile
import unittest
from prepare_github_release import prepare, sha256


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.candidate = self.root / 'candidate'
        self.candidate.mkdir()
        self.output = self.root / 'release'
        self.manifest = {'id': 'Librarian', 'version': '1.0-beta3', 'min_game_version': '0.111.0',
                         'has_dll': True, 'has_pck': True,
                         'dependencies': [{'id': 'BaseLib', 'min_version': '3.4.5'},
                                          {'id': 'STS2-RitsuLib', 'min_version': '0.6.2'}]}
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        for name in ['Librarian.dll', 'Librarian.pck']:
            (self.candidate / name).write_bytes(b'test-only fixture, not game content')
        for name in ['LICENSE', 'ASSET-LICENSE.md', 'THIRD-PARTY-NOTICES.md']:
            (self.root / name).write_text('fixture notice')
        (self.root / 'licenses').mkdir()
        (self.root / 'licenses/dependency.txt').write_text('fixture dependency notice')

    def run_prepare(self, delivery=None):
        return prepare(self.candidate, '1.0-beta3', self.output, delivery, root=self.root)

    def test_valid_files_and_checksums(self):
        result = self.run_prepare()
        self.assertFalse(result['published'])
        for line in (self.output / 'SHA256SUMS.txt').read_text().splitlines():
            hash_value, name = line.split('  ', 1)
            self.assertEqual(hash_value, sha256(self.output / name))
        self.assertTrue((self.output / 'licenses-dependency.txt').exists())

    def test_extra_dependency_rejected(self):
        (self.candidate / 'BaseLib.dll').write_bytes(b'not allowed')
        with self.assertRaisesRegex(ValueError, 'exactly'):
            self.run_prepare()
        self.assertFalse(self.output.exists())

    def test_wrong_version_rejected(self):
        self.manifest['version'] = '0.6.1'
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, 'version mismatch'):
            self.run_prepare()

    def test_delivery_hashes(self):
        delivery = self.root / 'delivery.json'
        record = {'version': '1.0-beta3', 'build': {'artifacts': [
            {'name': p.name, 'sha256': sha256(p)} for p in self.candidate.iterdir()]}}
        delivery.write_text(json.dumps(record))
        self.assertTrue(self.run_prepare(delivery)['delivery_hashes_matched'])

    def test_mixed_build_rejected(self):
        delivery = self.root / 'delivery.json'
        delivery.write_text(json.dumps({'version': '1.0-beta3', 'build': {'artifacts': []}}))
        with self.assertRaisesRegex(ValueError, 'Delivery'):
            self.run_prepare(delivery)
        self.assertFalse(self.output.exists())

    def test_never_overwrite(self):
        self.output.mkdir()
        with self.assertRaisesRegex(ValueError, 'new directory'):
            self.run_prepare()

    def test_stable_requires_explicit_channel_and_validated_delivery(self):
        self.manifest['min_game_version'] = '0.107.1'
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, 'Compatibility'):
            self.run_prepare()
        with self.assertRaisesRegex(ValueError, 'requires current'):
            prepare(self.candidate, '1.0-beta3', self.output, root=self.root, channel='stable')
        delivery = self.root / 'delivery.json'
        record = {'version': '1.0-beta3', 'channel': 'stable', 'validation': {'passed': False},
                  'build': {'artifacts': [{'name': p.name, 'sha256': sha256(p)} for p in self.candidate.iterdir()]}}
        delivery.write_text(json.dumps(record))
        with self.assertRaisesRegex(ValueError, 'must certify'):
            prepare(self.candidate, '1.0-beta3', self.output, delivery, root=self.root, channel='stable')
        record['validation']['passed'] = True
        delivery.write_text(json.dumps(record))
        self.assertTrue(prepare(self.candidate, '1.0-beta3', self.output, delivery,
                                root=self.root, channel='stable')['delivery_hashes_matched'])

    def test_legacy_requires_evidence_and_keeps_manifest(self):
        self.manifest['dependencies'] = [{'id': 'BaseLib', 'min_version': '3.4.5'}]
        path = self.candidate / 'Librarian.json'
        path.write_text(json.dumps(self.manifest))
        before = path.read_bytes()
        with self.assertRaisesRegex(ValueError, 'Dependency'):
            self.run_prepare()
        with self.assertRaisesRegex(ValueError, 'requires verified'):
            prepare(self.candidate, '1.0-beta3', self.output, root=self.root, historical=True)
        delivery = self.root / 'delivery.json'
        delivery.write_text(json.dumps({'version': '1.0-beta3', 'build': {'artifacts': [
            {'name': p.name, 'sha256': sha256(p)} for p in self.candidate.iterdir()]}}))
        prepare(self.candidate, '1.0-beta3', self.output, delivery, root=self.root, historical=True)
        self.assertEqual(before, (self.output / 'Librarian.json').read_bytes())


if __name__ == '__main__':
    unittest.main()
