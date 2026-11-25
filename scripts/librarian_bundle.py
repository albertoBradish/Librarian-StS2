"""Validate the complete, owned Librarian bundle before deployment or publication."""
import hashlib
import json
import re
from pathlib import Path, PurePosixPath

GAME_TARGETS = ('0.107.1', '0.111.0')
BUNDLE_FILES = ('Librarian.dll', 'Librarian.json', 'Librarian.pck',
                'librarian-variants.manifest', *(f'compat/{v}/Librarian.dll' for v in GAME_TARGETS))


def version_number(mod_version):
    match = re.fullmatch(r'(\d+)\.(\d+)\.(\d+)(?:-fix[1-9]\d*)?', str(mod_version))
    if match is None or tuple(map(int, match.groups())) < (1, 3, 0):
        raise ValueError('Unsupported unified Librarian version')
    return tuple(map(int, match.groups()))


def required_ritsulib_version(mod_version):
    version = version_number(mod_version)
    if version >= (1, 4, 8):
        return '0.6.13'
    if version >= (1, 4, 7):
        return '0.6.12'
    return '0.6.10' if version >= (1, 4, 5) else '0.6.4'


def game_targets_for(mod_version):
    version = version_number(mod_version)
    if version >= (1, 4, 11):
        return (*GAME_TARGETS, '0.111.1', '0.113.0', '0.113.1', '0.114.0')
    if version >= (1, 4, 9):
        return (*GAME_TARGETS, '0.111.1', '0.113.0', '0.113.1')
    if version >= (1, 4, 8):
        return (*GAME_TARGETS, '0.113.0', '0.113.1')
    return (*GAME_TARGETS, '0.113.0') if version >= (1, 4, 7) else GAME_TARGETS


def bundle_files_for(mod_version):
    return (*BUNDLE_FILES, *(f'compat/{v}/Librarian.dll' for v in game_targets_for(mod_version) if v not in GAME_TARGETS))


def native_bootstrap_fields(mod_version):
    if version_number(mod_version) >= (1, 4, 6):
        return dict(has_pck=False, min_game_version='0.0.0', dependencies=[])
    return dict(has_pck=True, min_game_version=GAME_TARGETS[0],
                dependencies=[dict(id='STS2-RitsuLib', min_version=required_ritsulib_version(mod_version))])


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
    if 'Librarian.json' not in files:
        raise ValueError('Bundle is missing its native manifest')
    mod = json.loads((directory / 'Librarian.json').read_text(encoding='utf-8-sig'))
    names = bundle_files_for(mod.get('version'))
    targets = game_targets_for(mod.get('version'))
    if files != set(names):
        raise ValueError('Bundle file allowlist mismatch: ' + repr(sorted(files ^ set(names))))
    variants = json.loads((directory / 'librarian-variants.manifest').read_text(encoding='utf-8-sig'))
    if mod.get('id') != 'Librarian' or mod.get('has_dll') is not True:
        raise ValueError('Invalid Librarian native manifest')
    native = native_bootstrap_fields(mod.get('version'))
    if mod.get('min_game_version') != native['min_game_version'] or mod.get('has_pck') is not native['has_pck']:
        raise ValueError('Native minimum game version does not match bundle targets')
    minimum = required_ritsulib_version(mod.get('version'))
    if mod.get('dependencies') != native['dependencies']:
        raise ValueError(f'Unified Librarian {mod.get("version")} has invalid native dependencies (content needs RitsuLib >={minimum})')
    if (variants.get('schema_version'), variants.get('mod_id'), variants.get('mod_version')) != (1, 'Librarian', mod.get('version')):
        raise ValueError('Bundle/native manifest identity mismatch')
    entries = variants.get('variants', [])
    if len(entries) != len(targets) or {e.get('game_version') for e in entries} != set(targets):
        raise ValueError('Bundle must contain exactly the approved game API targets')
    for entry in entries:
        name = f"compat/{entry['game_version']}/Librarian.dll"
        if entry.get('path') != name or entry.get('assembly') != 'Librarian':
            raise ValueError('Invalid content assembly identity or path')
        if hashlib.sha256((directory / name).read_bytes()).hexdigest().lower() != str(entry.get('sha256', '')).lower():
            raise ValueError('Content assembly hash mismatch: ' + name)
    identities = [file_identity(directory / n, n) for n in names]
    if expected_artifacts is not None:
        for item in expected_artifacts:
            name = item.get('name', '')
            p = PurePosixPath(name)
            if not name or p.is_absolute() or '..' in p.parts or '\\' in name:
                raise ValueError('Unsafe artifact path')
        if sorted(identities, key=lambda x: x['name']) != sorted(expected_artifacts, key=lambda x: x['name']):
            raise ValueError('Bundle differs from recorded build artifacts')
    return identities
