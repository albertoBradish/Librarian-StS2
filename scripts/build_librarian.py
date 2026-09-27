"""Build/package the mod with private tooling and a reproducible log; never deploy to Steam."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import shutil
from run_research_game import isolated_environment
from build_provenance import source_manifest, environment_manifest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'src/Librarian.Mod'
SDK = Path(os.environ.get('LIBRARIAN_DOTNET_ROOT', str(ROOT / '.research/tools/dotnet')))
OUTPUT = ROOT / '.research/compatibility-game/mods/Librarian'
LOGS = ROOT / '.research/logs'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pack', action='store_true')
    args = parser.parse_args()
    LOGS.mkdir(parents=True, exist_ok=True)
    env = isolated_environment(ROOT / '.research/librarian-build-userdata')
    env['DOTNET_ROOT'] = str(SDK)
    env['PATH'] = str(SDK) + os.pathsep + env.get('PATH', '')
    env['MSBUILDDISABLENODEREUSE'] = '1'
    # Keep NuGet's bookkeeping inside the workspace, using the existing cache offline.
    packages = ROOT / '.research/nuget-packages'
    if not packages.exists():
        shutil.copytree(Path.home() / '.nuget/packages', packages)
    env['NUGET_PACKAGES'] = str(packages)
    env['DOTNET_CLI_HOME'] = str(ROOT / '.research/librarian-build-userdata')
    provenance = {'source_before': source_manifest(), 'environment': environment_manifest(SDK, env)}
    if args.pack:
        # The editor resolves the native base classes of local Godot scripts here.
        # Keep these read-only dependency copies in ignored build cache only;
        # the deployment allowlist below never includes them.
        export_cache = PROJECT / '.godot/mono/temp/bin/Debug'
        export_cache.mkdir(parents=True, exist_ok=True)
        game_data = ROOT / '.research/compatibility-game/data_sts2_windows_x86_64'
        for dependency in [*(p for p in game_data.glob('*.dll') if not p.name.startswith('GodotSharp')),
                           ROOT / '.research/compatibility-game/mods/BaseLib/BaseLib.dll']:
            shutil.copy2(dependency, export_cache / dependency.name)
        ritsu = ROOT / '.research/tools/ritsulib/v0.6.2'
        for dependency in [*(ritsu / 'compat/0.107.1').glob('*.dll'), *(ritsu / 'shared').glob('*.dll')]:
            shutil.copy2(dependency, export_cache / dependency.name)
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    action = 'publish' if args.pack else 'build'
    log = LOGS / f'librarian-{action}-{stamp}.log'
    command = [str(SDK / 'dotnet.exe'), action, str(PROJECT / 'Librarian.csproj'), '-c', 'Debug', '--nologo', '-v', 'minimal']
    with log.open('wb') as stream:
        result = subprocess.run(command, cwd=PROJECT, env=env, stdout=stream, stderr=subprocess.STDOUT,
                                creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise SystemExit(f'Build failed ({result.returncode}); inspect {log}')
    artifacts = []
    for name in ['Librarian.dll','Librarian.json'] + (['Librarian.pck'] if args.pack else []):
        file = OUTPUT / name
        artifacts.append({'name':name,'bytes':file.stat().st_size,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
    provenance['source_after'] = source_manifest()
    if provenance['source_before']['sha256'] != provenance['source_after']['sha256']:
        (LOGS / f'librarian-input-drift-{stamp}.json').write_text(json.dumps(provenance, indent=2), encoding='utf-8')
        raise SystemExit('Build inputs changed during compilation/export; latest candidate was not updated. Inspect input-drift evidence and rebuild after edits stop.')
    identity = {'log':str(log),'command':command,'artifacts':artifacts,'provenance':provenance}
    (LOGS / f'librarian-build-{stamp}.json').write_text(json.dumps(identity, indent=2),encoding='utf-8')
    (LOGS / 'librarian-build-latest.json').write_text(json.dumps(identity, indent=2),encoding='utf-8')
    print(f'PASS {action}: {log}')

if __name__ == '__main__':
    main()
