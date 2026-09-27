"""Prepare mod-only GitHub assets locally; never create tags, publish, or upload."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = {'Librarian.dll', 'Librarian.json', 'Librarian.pck'}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(candidate, version, output, delivery=None, root=ROOT, historical=False, channel='beta'):
    candidate, output, root = Path(candidate).resolve(), Path(output).resolve(), Path(root).resolve()
    if not re.fullmatch(r'\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9.]+)?', version):
        raise ValueError('Invalid release version')
    if output.exists():
        raise ValueError('Output must be a new directory; existing releases are never overwritten')
    if output == candidate or candidate in output.parents or output in candidate.parents:
        raise ValueError('Output must be separate from candidate')
    if {p.name for p in candidate.iterdir()} != RUNTIME:
        raise ValueError('Candidate must contain exactly Librarian.dll, Librarian.json, Librarian.pck')
    if any((candidate / name).is_symlink() or not (candidate / name).is_file()
           or (candidate / name).stat().st_size == 0 for name in RUNTIME):
        raise ValueError('Runtime files must be nonempty regular files')
    manifest = json.loads((candidate / 'Librarian.json').read_text(encoding='utf-8-sig'))
    if manifest.get('id') != 'Librarian' or manifest.get('version') != version:
        raise ValueError('Manifest identity/version mismatch')
    game_versions = {'beta': '0.111.0', 'stable': '0.107.1'}
    if channel not in game_versions or manifest.get('min_game_version') != game_versions[channel]:
        raise ValueError('Compatibility changed; review preparation policy before releasing')
    if channel == 'stable' and (not delivery or historical):
        raise ValueError('Stable preparation requires current verified Delivery evidence')
    deps = {d['id']: d['min_version'] for d in manifest.get('dependencies', [])}
    current_deps = {'BaseLib': '3.4.5', 'STS2-RitsuLib': '0.6.2'}
    if historical and not delivery:
        raise ValueError('Historical preparation requires verified artifact evidence')
    if deps != current_deps and not (historical and deps == {'BaseLib': '3.4.5'}):
        raise ValueError('Dependency versions changed; review policy before releasing')
    if not manifest.get('has_dll') or not manifest.get('has_pck'):
        raise ValueError('Runtime manifest must declare DLL and PCK')
    actual = {name: sha256(candidate / name) for name in RUNTIME}
    if delivery:
        record = json.loads(Path(delivery).read_text(encoding='utf-8-sig'))
        expected = {f['name']: f['sha256'].lower() for f in record['build']['artifacts']}
        if record['version'] != version or expected != actual:
            raise ValueError('Candidate differs from versioned Delivery evidence')
        if channel == 'stable' and (record.get('channel') != 'stable'
                                   or not record.get('validation', {}).get('passed')):
            raise ValueError('Stable Delivery must certify the stable channel and runtime validation')
    asset_license = root / 'ASSET-LICENSE.md'
    if not asset_license.exists():
        asset_license = root / 'github/ASSET-LICENSE.md'
    sources = {name: candidate / name for name in sorted(RUNTIME)}
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
    sums = ''.join(f'{sha256(output / name)}  {name}\n' for name in sorted(sources))
    (output / 'SHA256SUMS.txt').write_text(sums, encoding='utf-8')
    return {'version': version, 'output': str(output), 'runtime_hashes': actual,
            'delivery_hashes_matched': bool(delivery), 'published': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--delivery', type=Path)
    parser.add_argument('--channel', choices=['beta', 'stable'], default='beta')
    parser.add_argument('--historical', action='store_true', help='Allow evidence-backed BaseLib-only historical releases; does not imply runtime approval.')
    args = parser.parse_args()
    print(json.dumps(prepare(args.candidate, args.version, args.output, args.delivery, historical=args.historical, channel=args.channel), indent=2))


if __name__ == '__main__':
    main()
