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
             ROOT / 'scripts/run_research_game.py']
    for root in [ROOT / 'src', ROOT / 'tests', ROOT / 'data']:
        paths.extend(p for p in root.rglob('*') if p.is_file() and not EXCLUDED.intersection(p.relative_to(root).parts)
                     and p.suffix.lower() not in GENERATED_SUFFIXES)
    entries = [fingerprint(p) for p in sorted(set(paths)) if p.is_file()]
    digest = hashlib.sha256(json.dumps(entries, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return {'sha256': digest, 'files': entries}

def environment_manifest(sdk, env):
    result = subprocess.run([str(sdk / 'dotnet.exe'), '--version'], env=env, cwd=ROOT, capture_output=True, text=True, check=True)
    names = ['.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64_console.exe',
             '.research/ritsu-game/data_sts2_windows_x86_64/sts2.dll',
             '.research/tools/ritsulib/v0.6.4/RitsuLib.References.props',
             'src/Librarian.Mod/Directory.Build.props']
    paths = [ROOT / n for n in names]
    paths.extend((ROOT / '.research/tools/ritsulib/v0.6.4').rglob('*.dll'))
    git = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True)
    return {'dotnet_sdk': result.stdout.strip(), 'git_head': git.stdout.strip() if git.returncode == 0 else None,
            'note': 'Source tree hash identifies working inputs; git HEAD alone may be older. Private dependency identities only, no extracted content.',
            'dependencies': [fingerprint(p) for p in sorted(set(paths)) if p.is_file()]}
