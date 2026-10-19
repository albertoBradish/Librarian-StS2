"""Verify release preparation rejects mixed, unapproved or extra runtime files."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from prepare_github_release import prepare, sha256
from prepare_github_release import UNIFIED_TARGETS
try:
    from librarian_bundle import BUNDLE_FILES, validate_bundle
except ModuleNotFoundError as error:
    if error.name != 'librarian_bundle':
        raise
    BUNDLE_FILES = ()  # Historical three-file source exports do not include the unified helper.


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

    def test_current_pinned_ritsu_bundle(self):
        self.manifest['dependencies'][1]['min_version'] = '0.6.4'
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        self.assertFalse(self.run_prepare()['published'])

    def test_extra_dependency_rejected(self):
        (self.candidate / 'BaseLib.dll').write_bytes(b'not allowed')
        with self.assertRaisesRegex(ValueError, 'exactly'):
            self.run_prepare()
        self.assertFalse(self.output.exists())

    def test_ritsu_only_requires_matching_native_evidence(self):
        self.manifest['dependencies'] = [{'id': 'STS2-RitsuLib', 'min_version': '0.6.4'}]
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, 'requires verified'):
            self.run_prepare()
        delivery = self.root / 'delivery.json'
        record = {'version': '1.0-beta3', 'channel': 'stable', 'validation': {'passed': True},
                  'build': {'artifacts': [{'name': p.name, 'sha256': sha256(p)} for p in self.candidate.iterdir()]}}
        delivery.write_text(json.dumps(record))
        with self.assertRaisesRegex(ValueError, 'must certify'):
            self.run_prepare(delivery)
        record['channel'] = 'beta'
        delivery.write_text(json.dumps(record))
        self.assertFalse(self.run_prepare(delivery)['published'])
        self.assertEqual({d['id'] for d in json.loads((self.output / 'Librarian.json').read_text())['dependencies']}, {'STS2-RitsuLib'})

    def test_ritsu_only_wrong_channel_bundle_rejected(self):
        self.manifest['dependencies'] = [{'id': 'STS2-RitsuLib', 'min_version': '0.6.2'}]
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, 'Dependency'):
            self.run_prepare()

    def test_stable_ritsu_only_with_native_evidence(self):
        self.manifest['min_game_version'] = '0.107.1'
        self.manifest['dependencies'] = [{'id': 'STS2-RitsuLib', 'min_version': '0.6.2'}]
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.manifest))
        delivery = self.root / 'delivery.json'
        delivery.write_text(json.dumps({'version': '1.0-beta3', 'channel': 'stable', 'validation': {'passed': True},
                                       'build': {'artifacts': [{'name': p.name, 'sha256': sha256(p)} for p in self.candidate.iterdir()]}}))
        self.assertFalse(prepare(self.candidate, '1.0-beta3', self.output, delivery, root=self.root, channel='stable')['published'])

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


@unittest.skipUnless(BUNDLE_FILES, 'Unified helper is absent from this historical source export')
class UnifiedReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.candidate = self.root / 'candidate'
        self.candidate.mkdir()
        self.output = self.root / 'release'
        self.native = {'id': 'Librarian', 'version': '1.3.0', 'min_game_version': '0.107.1',
                       'has_dll': True, 'has_pck': True,
                       'dependencies': [{'id': 'STS2-RitsuLib', 'min_version': '0.6.4'}]}
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.native), encoding='utf-8')
        for name in BUNDLE_FILES:
            if name in {'Librarian.json', 'librarian-variants.manifest'}:
                continue
            path = self.candidate / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(('owned test fixture ' + name).encode())
        variants = [{'game_version': target['version'], 'assembly': 'Librarian',
                     'path': f"compat/{target['version']}/Librarian.dll",
                     'sha256': sha256(self.candidate / f"compat/{target['version']}/Librarian.dll")}
                    for target in UNIFIED_TARGETS]
        (self.candidate / 'librarian-variants.manifest').write_text(json.dumps({
            'schema_version': 1, 'mod_id': 'Librarian', 'mod_version': '1.3.0', 'variants': variants}), encoding='utf-8')
        (self.root / 'licenses').mkdir()
        (self.root / 'licenses/ritsu.txt').write_text('fixture MIT notice')
        for name in ['LICENSE', 'ASSET-LICENSE.md', 'THIRD-PARTY-NOTICES.md']:
            (self.root / name).write_text('fixture license')
        (self.root / 'global.json').write_text('{}')
        self.record = {'channel': 'unified', 'version': '1.3.0', 'game_targets': UNIFIED_TARGETS,
                       'dependencies': {'RitsuLib': '0.6.4'},
                       'validation': {'passed': True, 'targets': {target['version']: {'passed': True} for target in UNIFIED_TARGETS}},
                       'build': {'artifacts': validate_bundle(self.candidate)}}
        self.set_source_provenance([self.root / 'global.json'])
        self.delivery = self.root / 'delivery.json'
        self.write_delivery()

    def write_delivery(self):
        self.delivery.write_text(json.dumps(self.record), encoding='utf-8')

    def set_source_provenance(self, paths):
        files = [{'path': path.relative_to(self.root).as_posix(), 'bytes': path.stat().st_size, 'sha256': sha256(path)}
                 for path in sorted(paths)]
        digest = hashlib.sha256(json.dumps(files, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        snapshot = {'files': files, 'sha256': digest}
        self.record['build']['provenance'] = {'source_before': snapshot, 'source_after': json.loads(json.dumps(snapshot))}

    def run_prepare(self):
        return prepare(self.candidate, '1.3.0', self.output, self.delivery, root=self.root, channel='unified')

    def test_complete_bundle_flat_assets_install_mapping_and_sha(self):
        result = self.run_prepare()
        self.assertFalse(result['published'])
        mapping = json.loads((self.output / 'INSTALL-MAP.json').read_text())
        self.assertEqual(mapping['compat-0.107.1-Librarian.dll'], 'mods/Librarian/compat/0.107.1/Librarian.dll')
        self.assertEqual(mapping['compat-0.111.0-Librarian.dll'], 'mods/Librarian/compat/0.111.0/Librarian.dll')
        self.assertEqual(len(mapping), 6)
        for asset, installation in mapping.items():
            self.assertEqual((self.output / asset).read_bytes(), (self.candidate / installation.removeprefix('mods/Librarian/')).read_bytes())
        for line in (self.output / 'SHA256SUMS.txt').read_text().splitlines():
            value, name = line.split('  ', 1)
            self.assertEqual(value, sha256(self.output / name))
        self.assertFalse(list(self.output.glob('*.zip')))

    def test_later_minor_version_keeps_exact_manifest_delivery_and_install_version(self):
        self.native['version'] = '1.4.0'
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.native), encoding='utf-8')
        path = self.candidate / 'librarian-variants.manifest'
        variants = json.loads(path.read_text())
        variants['mod_version'] = '1.4.0'
        path.write_text(json.dumps(variants), encoding='utf-8')
        self.record['version'] = '1.4.0'
        self.record['build']['artifacts'] = validate_bundle(self.candidate)
        self.write_delivery()
        result = prepare(self.candidate, '1.4.0', self.output, self.delivery, root=self.root, channel='unified')
        self.assertEqual(result['version'], '1.4.0')
        self.assertTrue((self.output / 'INSTALL.txt').read_text().startswith('Librarian V1.4.0 /'))
        with self.assertRaisesRegex(ValueError, 'exact unified'):
            prepare(self.candidate, '1.3.0', self.root / 'mismatched', self.delivery, root=self.root, channel='unified')

    def test_requires_delivery_and_both_native_proofs(self):
        with self.assertRaisesRegex(ValueError, 'requires current'):
            prepare(self.candidate, '1.3.0', self.output, root=self.root, channel='unified')
        self.record['validation']['targets']['0.107.1']['passed'] = False
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'both targets'):
            self.run_prepare()
        self.assertFalse(self.output.exists())

    def test_reject_wrong_game_commit_and_branch(self):
        self.record['game_targets'] = [dict(target) for target in UNIFIED_TARGETS]
        self.record['game_targets'][0]['commit'] = '41cef1ea'
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'exact game'):
            self.run_prepare()

    def test_reject_unexpected_delivery_dependency(self):
        self.record['dependencies']['OtherMod'] = '1.0.0'
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'only the pinned RitsuLib'):
            self.run_prepare()

    def test_reject_extra_private_dependency(self):
        (self.candidate / 'sts2.dll').write_bytes(b'private input')
        with self.assertRaisesRegex(ValueError, 'allowlist'):
            self.run_prepare()

    def test_reject_unselected_content_corruption(self):
        (self.candidate / 'compat/0.107.1/Librarian.dll').write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            self.run_prepare()

    def test_reject_manifest_identity_mismatch(self):
        self.native['version'] = '1.2.0-beta13'
        (self.candidate / 'Librarian.json').write_text(json.dumps(self.native), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'identity mismatch'):
            self.run_prepare()

    def test_reject_missing_full_source_provenance(self):
        del self.record['build']['provenance']
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'source_before'):
            self.run_prepare()

    def test_reject_source_summary_digest_mismatch(self):
        self.record['build']['provenance']['source_after']['sha256'] = '0' * 64
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'canonical file summary'):
            self.run_prepare()
        self.assertFalse(self.output.exists())

    def test_reject_missing_before_summary(self):
        del self.record['build']['provenance']['source_before']
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'source_before'):
            self.run_prepare()

    def test_reject_source_changed_during_build(self):
        before = self.record['build']['provenance']['source_before']
        before['files'][0]['sha256'] = '0' * 64
        before['sha256'] = hashlib.sha256(json.dumps(before['files'], sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'source changed'):
            self.run_prepare()

    def test_reject_incomplete_source_fingerprint(self):
        del self.record['build']['provenance']['source_after']['files'][0]['bytes']
        self.write_delivery()
        with self.assertRaisesRegex(ValueError, 'Invalid source provenance fingerprint'):
            self.run_prepare()

    def test_reject_legacy_channel_for_nested_bundle(self):
        with self.assertRaisesRegex(ValueError, 'exactly'):
            prepare(self.candidate, '1.3.0', self.output, self.delivery, root=self.root, channel='beta')

    @unittest.skipUnless((Path(__file__).parent / 'export_github_source.py').is_file(), 'Private source exporter is not part of the public build checkout')
    def test_unified_export_binds_source_and_complete_candidate(self):
        import export_github_source as exporter
        from unittest.mock import patch
        (self.root / 'docs').mkdir()
        (self.root / 'docs/Current-Release.json').write_text(json.dumps({'working_version': '1.3.0'}))
        mod_source = self.root / 'src/Librarian.Mod'
        mod_source.mkdir(parents=True)
        (mod_source / 'Librarian.json').write_bytes((self.candidate / 'Librarian.json').read_bytes())
        (mod_source / 'Librarian.csproj').write_text('<Project><PropertyGroup><Version>1.3.0</Version><RitsuLibReferenceTarget>$(LibrarianGameVersion)</RitsuLibReferenceTarget></PropertyGroup><Import Project="../../.research/tools/ritsulib/v0.6.4/RitsuLib.References.props" /></Project>')
        loader = self.root / 'src/Librarian.Loader/Librarian.Loader.csproj'
        loader.parent.mkdir()
        loader.write_text('<Project />')
        self.record['candidate'] = 'candidate'
        self.record['active_card_count'] = 91
        self.set_source_provenance([self.root / 'global.json', mod_source / 'Librarian.csproj', mod_source / 'Librarian.json', loader])
        self.write_delivery()
        with patch.object(exporter, 'ROOT', self.root):
            result = exporter.export_inputs('unified', '.', 'delivery.json')
            self.assertEqual(result[2], '1.3.0')
            self.assertEqual(len(result[3]), 6)
            self.assertIn('src/Librarian.Loader/Librarian.Loader.csproj', result[-1])
            with self.assertRaisesRegex(ValueError, 'paired'):
                exporter.export_inputs('unified')
            (mod_source / 'Librarian.csproj').write_text('<Project />')
            with self.assertRaisesRegex(ValueError, 'select its API'):
                exporter.export_inputs('unified', '.', 'delivery.json')


if __name__ == '__main__':
    unittest.main()
