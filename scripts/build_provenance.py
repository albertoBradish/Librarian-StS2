"""Fingerprint owned build inputs and private dependency identities without copying game binaries."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {'.godot', 'bin', 'obj', '__pycache__', 'node_modules', '.git', '.idea', '.vs'}
GENERATED_SUFFIXES = {'.uid'}  # Godot emits these during editor import; their contents are not authored build inputs.

def fingerprint(path):
    return {'path': path.relative_to(ROOT).as_posix(), 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

def source_manifest():
    paths = [ROOT / 'global.json', ROOT / 'scripts/build_librarian.py', ROOT / 'scripts/build_provenance.py',
             ROOT / 'scripts/run_research_game.py', ROOT / 'scripts/game_test_audio.py', ROOT / 'scripts/build_unified_librarian.py',
             ROOT / 'scripts/ritsulib-build-inputs.json', ROOT / 'scripts/librarian_bundle.py', ROOT / 'scripts/run_unified_game.py',
             ROOT / 'scripts/dependency_upgrade_run.py',
             ROOT / 'scripts/inspect_owned_pck.py', ROOT / 'scripts/repack_owned_png.py',
             ROOT / 'scripts/pack_exact_payloads.gd.in']
    for root in [ROOT / 'src', ROOT / 'tests', ROOT / 'data']:
        paths.extend(p for p in root.rglob('*') if p.is_file() and not EXCLUDED.intersection(p.relative_to(root).parts)
                     and p.suffix.lower() not in GENERATED_SUFFIXES)
    entries = [fingerprint(p) for p in sorted(set(paths)) if p.is_file()]
    digest = hashlib.sha256(json.dumps(entries, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return {'sha256': digest, 'files': entries}

def environment_manifest(sdk, env, runtime_ritsu=None):
    result = subprocess.run([str(sdk / 'dotnet.exe'), '--version'], env=env, cwd=ROOT, capture_output=True, text=True, check=True)
    from librarian_bundle import game_targets_for, required_ritsulib_version
    version = json.loads((ROOT / 'src/Librarian.Mod/Librarian.json').read_text(encoding='utf-8-sig'))['version']
    ritsu = ROOT / f'.research/tools/ritsulib/v{required_ritsulib_version(version)}'
    names = ['.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64_console.exe',
             '.research/ritsu-game/data_sts2_windows_x86_64/sts2.dll',
             '.research/channels/stable/workspace/.research/ritsu-game/data_sts2_windows_x86_64/sts2.dll',
             'src/Librarian.Mod/Directory.Build.props',
             '.research/ritsu-game/libspine_godot.windows.template_release.x86_64.dll',
             '.research/channels/stable/workspace/.research/ritsu-game/libspine_godot.windows.template_release.x86_64.dll']
    paths = [ROOT / n for n in names]
    if '0.111.1' in game_targets_for(version):
        main = ROOT / '.research/game-sdk-20261116/steam-0.111.1'
        paths.extend(main / name for name in ('release_info.json', 'data_sts2_windows_x86_64/sts2.dll'))
    if '0.113.0' in game_targets_for(version):
        beta = ROOT / '.research/game-sdk-20261112/steam-0.113.0'
        paths.extend(beta / name for name in ('release_info.json', 'data_sts2_windows_x86_64/sts2.dll'))
    if '0.113.1' in game_targets_for(version):
        hotfix = ROOT / '.research/game-sdk-20261114/steam-0.113.1'
        paths.extend(hotfix / name for name in ('release_info.json', 'data_sts2_windows_x86_64/sts2.dll'))
    if '0.114.0' in game_targets_for(version):
        beta = ROOT / '.research/game-sdk-20261125/steam-0.114.0'
        paths.extend(beta / name for name in ('release_info.json', 'data_sts2_windows_x86_64/sts2.dll'))
        if runtime_ritsu != ROOT / '.research/tools/ritsulib/v0.6.16':
            paths.extend(path for path in (ROOT / '.research/tools/ritsulib/v0.6.16').rglob('*') if path.is_file())
    if runtime_ritsu is not None and runtime_ritsu != ritsu:
        paths.extend(path for path in runtime_ritsu.rglob('*') if path.is_file())
    paths.extend(ritsu / name for name in ('RitsuLib.References.props', 'mod_manifest.json', 'ritsulib-variants.manifest', 'assets.zip'))
    paths.extend(ritsu.rglob('*.dll'))
    git = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True)
    return {'dotnet_sdk': result.stdout.strip(), 'git_head': git.stdout.strip() if git.returncode == 0 else None,
            'note': 'Source tree hash identifies working inputs; git HEAD alone may be older. Private dependency identities only, no extracted content.',
            'dependencies': [fingerprint(p) for p in sorted(set(paths)) if p.is_file()]}
