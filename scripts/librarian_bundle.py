"""Validate the complete, owned Librarian bundle before deployment or publication."""
import hashlib
import json
from pathlib import Path, PurePosixPath

GAME_TARGETS = ('0.107.1', '0.111.0')
BUNDLE_FILES = ('Librarian.dll', 'Librarian.json', 'Librarian.pck',
                'librarian-variants.manifest', *(f'compat/{v}/Librarian.dll' for v in GAME_TARGETS))


def file_identity(path, name):
    return {'name': name, 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def validate_bundle(directory, expected_artifacts=None):
    directory = Path(directory).resolve()
    files = set()
    for path in directory.rglob('*'):
        if path.is_symlink() or path.is_junction():
            raise ValueError('Bundle cannot contain links: ' + str(path))
        if path.is_file():
            files.add(path.relative_to(directory).as_posix())
    if files != set(BUNDLE_FILES):
        raise ValueError('Bundle file allowlist mismatch: ' + repr(sorted(files ^ set(BUNDLE_FILES))))
    mod = json.loads((directory / 'Librarian.json').read_text(encoding='utf-8-sig'))
    variants = json.loads((directory / 'librarian-variants.manifest').read_text(encoding='utf-8-sig'))
    if mod.get('id') != 'Librarian' or not mod.get('has_dll') or not mod.get('has_pck'):
        raise ValueError('Invalid Librarian native manifest')
    if mod.get('min_game_version') != GAME_TARGETS[0]:
        raise ValueError('Native minimum game version does not match bundle targets')
    if mod.get('dependencies') != [{'id': 'STS2-RitsuLib', 'min_version': '0.6.4'}]:
        raise ValueError('Unified Librarian requires only RitsuLib >=0.6.4')
    if (variants.get('schema_version'), variants.get('mod_id'), variants.get('mod_version')) != (1, 'Librarian', mod.get('version')):
        raise ValueError('Bundle/native manifest identity mismatch')
    entries = variants.get('variants', [])
    if len(entries) != 2 or {e.get('game_version') for e in entries} != set(GAME_TARGETS):
        raise ValueError('Bundle must contain exactly the two approved game API targets')
    for entry in entries:
        name = f"compat/{entry['game_version']}/Librarian.dll"
        if entry.get('path') != name or entry.get('assembly') != 'Librarian':
            raise ValueError('Invalid content assembly identity or path')
        if hashlib.sha256((directory / name).read_bytes()).hexdigest().lower() != str(entry.get('sha256', '')).lower():
            raise ValueError('Content assembly hash mismatch: ' + name)
    identities = [file_identity(directory / n, n) for n in BUNDLE_FILES]
    if expected_artifacts is not None:
        for item in expected_artifacts:
            name = item.get('name', '')
            p = PurePosixPath(name)
            if not name or p.is_absolute() or '..' in p.parts or '\\' in name:
                raise ValueError('Unsafe artifact path')
        if sorted(identities, key=lambda x: x['name']) != sorted(expected_artifacts, key=lambda x: x['name']):
            raise ValueError('Bundle differs from recorded build artifacts')
    return identities
