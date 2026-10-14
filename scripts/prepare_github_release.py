"""Prepare mod-only GitHub assets locally; never create tags, publish, or upload."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = {'Librarian.dll', 'Librarian.json', 'Librarian.pck'}
UNIFIED_TARGETS = [
    {'version': '0.107.1', 'commit': '59260271', 'branch': 'public'},
    {'version': '0.111.0', 'commit': '41cef1ea', 'branch': 'public-beta'},
]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_source_provenance(provenance):
    """Check the builder's canonical summaries and reject incomplete or drifting evidence."""
    if not isinstance(provenance, dict):
        raise ValueError('Unified Delivery requires complete build.provenance source_before/source_after snapshots')
    for stage in ('source_before', 'source_after'):
        snapshot = provenance.get(stage, {})
        if not isinstance(snapshot, dict):
            raise ValueError(f'Unified Delivery requires complete build.provenance.{stage}')
        files = snapshot.get('files')
        if not isinstance(files, list) or not files:
            raise ValueError(f'Unified Delivery requires complete build.provenance.{stage}.files')
        names = set()
        for item in files:
            if not isinstance(item, dict) or set(item) != {'path', 'bytes', 'sha256'}:
                raise ValueError('Invalid source provenance fingerprint')
            name = item['path']
            if not isinstance(name, str) or not name or name.startswith('/') or ':' in name or '\\' in name or any(
                    part in ('', '.', '..') for part in name.split('/')) or name in names:
                raise ValueError('Unsafe or duplicate source provenance path')
            names.add(name)
            if not isinstance(item['bytes'], int) or isinstance(item['bytes'], bool) or item['bytes'] < 0 or not isinstance(
                    item['sha256'], str) or not re.fullmatch(r'[0-9a-f]{64}', item['sha256']):
                raise ValueError('Invalid source provenance fingerprint')
        actual = hashlib.sha256(json.dumps(files, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        if snapshot.get('sha256') != actual:
            raise ValueError(f'Unified {stage}.sha256 differs from its canonical file summary')
    if provenance['source_before'] != provenance['source_after']:
        raise ValueError('Unified source changed between source_before and source_after')
    return provenance['source_after']['sha256']


def validate_unified_delivery(record, candidate, version):
    """Require both fixed native targets and bind the complete six-file candidate."""
    from librarian_bundle import validate_bundle
    if record.get('channel') != 'unified' or record.get('version') != version or version != '1.3.0':
        raise ValueError('Unified Delivery must identify unified 1.3.0')
    targets = record.get('game_targets')
    if not isinstance(targets, list) or sorted(targets, key=lambda x: x.get('version', '')) != UNIFIED_TARGETS:
        raise ValueError('Unified Delivery requires the two exact game version/commit/branch targets')
    dependencies = record.get('dependencies', {})
    if dependencies != {'RitsuLib': '0.6.4'}:
        raise ValueError('Unified Delivery requires only the pinned RitsuLib 0.6.4 baseline')
    validation = record.get('validation', {})
    proofs = validation.get('targets', {})
    if validation.get('passed') is not True or not isinstance(proofs, dict) or set(proofs) != {x['version'] for x in UNIFIED_TARGETS} or any(
            not isinstance(proofs[version], dict) or proofs[version].get('passed') is not True for version in proofs):
        raise ValueError('Unified Delivery must certify native validation for both targets')
    validate_source_provenance(record.get('build', {}).get('provenance', {}))
    artifacts = record.get('build', {}).get('artifacts')
    if not isinstance(artifacts, list) or len(artifacts) != 6:
        raise ValueError('Unified Delivery requires all six build artifacts')
    return validate_bundle(candidate, artifacts)


def prepare(candidate, version, output, delivery=None, root=ROOT, historical=False, channel='beta'):
    candidate, output, root = Path(candidate).resolve(), Path(output).resolve(), Path(root).resolve()
    if not re.fullmatch(r'\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9.]+)?', version):
        raise ValueError('Invalid release version')
    if output.exists():
        raise ValueError('Output must be a new directory; existing releases are never overwritten')
    if output == candidate or candidate in output.parents or output in candidate.parents:
        raise ValueError('Output must be separate from candidate')
    unified = channel == 'unified'
    unified_record = None
    if unified:
        if not delivery or historical:
            raise ValueError('Unified preparation requires current verified Delivery evidence')
        unified_record = json.loads(Path(delivery).read_text(encoding='utf-8-sig'))
        identities = validate_unified_delivery(unified_record, candidate, version)
        runtime = {item['name'] for item in identities}
    else:
        runtime = RUNTIME
    if not unified and {p.name for p in candidate.iterdir()} != runtime:
        raise ValueError('Candidate must contain exactly Librarian.dll, Librarian.json, Librarian.pck')
    if any((candidate / name).is_symlink() or not (candidate / name).is_file()
           or (candidate / name).stat().st_size == 0 for name in runtime):
        raise ValueError('Runtime files must be nonempty regular files')
    manifest = json.loads((candidate / 'Librarian.json').read_text(encoding='utf-8-sig'))
    if manifest.get('id') != 'Librarian' or manifest.get('version') != version:
        raise ValueError('Manifest identity/version mismatch')
    game_versions = {'beta': '0.111.0', 'stable': '0.107.1', 'unified': '0.107.1'}
    if channel not in game_versions or manifest.get('min_game_version') != game_versions[channel]:
        raise ValueError('Compatibility changed; review preparation policy before releasing')
    if channel == 'stable' and (not delivery or historical):
        raise ValueError('Stable preparation requires current verified Delivery evidence')
    deps = {d['id']: d['min_version'] for d in manifest.get('dependencies', [])}
    # Keep the verified 0.6.2 release policy and accept the current pinned 0.6.4 bundle.
    current_deps = [{'BaseLib': '3.4.5', 'STS2-RitsuLib': ritsu_version}
                    for ritsu_version in ('0.6.2', '0.6.4')]
    ritsu_only = {'STS2-RitsuLib': '0.6.2' if channel == 'stable' else '0.6.4'}
    current_deps.append(ritsu_only)
    if historical and not delivery:
        raise ValueError('Historical preparation requires verified artifact evidence')
    if deps not in current_deps and not (historical and deps == {'BaseLib': '3.4.5'}):
        raise ValueError('Dependency versions changed; review policy before releasing')
    if deps == ritsu_only and not delivery:
        raise ValueError('Ritsu-only preparation requires verified Delivery evidence')
    if not manifest.get('has_dll') or not manifest.get('has_pck'):
        raise ValueError('Runtime manifest must declare DLL and PCK')
    actual = {name: sha256(candidate / name) for name in runtime}
    if delivery:
        record = json.loads(Path(delivery).read_text(encoding='utf-8-sig'))
        expected = {f['name']: f['sha256'].lower() for f in record['build']['artifacts']}
        if record['version'] != version or expected != actual:
            raise ValueError('Candidate differs from versioned Delivery evidence')
        if deps == ritsu_only and (record.get('channel') != channel
                                  or not record.get('validation', {}).get('passed')):
            raise ValueError('Ritsu-only Delivery must certify its channel and runtime validation')
        if channel == 'stable' and (record.get('channel') != 'stable'
                                   or not record.get('validation', {}).get('passed')):
            raise ValueError('Stable Delivery must certify the stable channel and runtime validation')
    asset_license = root / 'ASSET-LICENSE.md'
    if not asset_license.exists():
        asset_license = root / 'github/ASSET-LICENSE.md'
    asset_map = {name: (name if '/' not in name else name.replace('/', '-')) for name in sorted(runtime)}
    sources = {asset_map[name]: candidate / name for name in sorted(runtime)}
    sources.update({'LICENSE': root / 'LICENSE', 'ASSET-LICENSE.md': asset_license,
                    'THIRD-PARTY-NOTICES.md': root / 'THIRD-PARTY-NOTICES.md'})
    # GitHub release assets have a flat namespace. Keep notice links usable after download.
    sources.update({f'licenses-{p.name}': p for p in (root / 'licenses').iterdir() if p.is_file()})
    if not sources['LICENSE'].is_file() or not any(x.startswith('licenses-') for x in sources):
        raise ValueError('License bundle missing')
    for source in sources.values():
        if not source.is_file() or source.is_symlink():
            raise ValueError(f'Missing/unsafe notice file: {source.name}')
    output.mkdir(parents=True)
    for name, source in sources.items():
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        if name.endswith('.md'):
            target.write_text(target.read_text(encoding='utf-8').replace('](licenses/', '](licenses-'), encoding='utf-8')
    if unified:
        install_map = {asset: 'mods/Librarian/' + relative for relative, asset in asset_map.items()}
        (output / 'INSTALL-MAP.json').write_text(json.dumps(install_map, indent=2) + '\n', encoding='utf-8')
        (output / 'INSTALL.txt').write_text(
            'Librarian V1.3.0 / game 0.107.1 and 0.111.0\n\n'
            'Install RitsuLib >=0.6.4 separately. Quit the game before installation.\n'
            'Copy each downloaded asset to the path listed in INSTALL-MAP.json, relative to the game directory.\n'
            'Create both compat subdirectories and rename their downloaded DLLs to Librarian.dll.\n'
            'Keep the root Librarian.dll, JSON, PCK, and variant manifest together. Keep your existing saves.\n'
            'For migration, subscribe to item 3801958367, quit, unsubscribe from 3811677053, wait for downloads, and restart.\n'
            'Do not enable both old and new Librarian copies. The loader selects the compatible content automatically.\n', encoding='utf-8')
        sources.update({'INSTALL-MAP.json': output / 'INSTALL-MAP.json', 'INSTALL.txt': output / 'INSTALL.txt'})
    sums = ''.join(f'{sha256(output / name)}  {name}\n' for name in sorted(sources))
    (output / 'SHA256SUMS.txt').write_text(sums, encoding='utf-8')
    return {'version': version, 'output': str(output), 'runtime_hashes': actual,
            'delivery_hashes_matched': bool(delivery), 'published': False,
            **({'channel': 'unified', 'installation_map': install_map} if unified else {})}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--delivery', type=Path)
    parser.add_argument('--channel', choices=['beta', 'stable', 'unified'], default='beta')
    parser.add_argument('--historical', action='store_true', help='Allow evidence-backed BaseLib-only historical releases; does not imply runtime approval.')
    args = parser.parse_args()
    print(json.dumps(prepare(args.candidate, args.version, args.output, args.delivery, historical=args.historical, channel=args.channel), indent=2))


if __name__ == '__main__':
    main()
