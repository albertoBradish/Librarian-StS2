"""Compile both fixed API targets, package one PCK, and deploy only isolated copies."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

from build_provenance import source_manifest, environment_manifest
from librarian_bundle import GAME_TARGETS, validate_bundle
from run_research_game import isolated_environment

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'src/Librarian.Mod'
SDK = ROOT / '.research/tools/dotnet'
MOD_VERSION = json.loads((PROJECT / 'Librarian.json').read_text(encoding='utf-8-sig'))['version']
WORK = ROOT / f'.research/unified-v{MOD_VERSION}'
LOGS = WORK / 'logs'
CANDIDATE = ROOT / f'outputs/revision-v{MOD_VERSION}/candidate/Librarian'
GAME_INPUTS = {
    '0.111.0': ROOT / '.research/ritsu-game',
    '0.107.1': ROOT / '.research/channels/stable/workspace/.research/ritsu-game',
}
GAME_HASHES = {
    '0.111.0': '0861bfa1df347538d932f22d580e75420f08082792eb914e53b4882764acdbe9',
    '0.107.1': 'a1f9e653f1e28e4076558fee1e60d218619cb7e057b887c6417f62c62c6d7a52',
}
RITSU = ROOT / '.research/tools/ritsulib/v0.6.4'


def run(command, env, label, cwd=ROOT):
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    log = LOGS / f'{label}-{stamp}.log'
    with log.open('wb') as stream:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT,
                                creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        print(log.read_text(encoding='utf-8-sig', errors='replace')[-18000:])
        raise SystemExit(f'Build failed ({result.returncode}); inspect {log}')
    print(f'PASS {label}: {log}', flush=True)
    return {'command': command, 'log': str(log.relative_to(ROOT)), 'exit_code': result.returncode}


def prepare_runtime(version):
    """Link immutable private game files; copy configuration and mod files independently."""
    source = GAME_INPUTS[version]
    target = WORK / 'runtimes' / version
    if hashlib.sha256((source / 'data_sts2_windows_x86_64/sts2.dll').read_bytes()).hexdigest() != GAME_HASHES[version]:
        raise ValueError('Game input hash changed: ' + version)
    for file in source.rglob('*'):
        relative = file.relative_to(source)
        if relative.parts[0] in ('mods', '.godot', 'logs', 'crashes') or not file.is_file():
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.exists():
            if file.suffix.lower() in ('.dll', '.exe', '.pck'):
                os.link(file, destination)
            else:
                shutil.copy2(file, destination)
    override = target / 'override.cfg'
    required = f'config/custom_user_dir_name="Sts2LibrarianUnified-{version}"'
    override.write_text('[application]\nconfig/name="Slay the Spire 2 - Librarian isolated"\n'
                        'config/use_custom_user_dir=true\n' + required + '\n'
                        '[sentry]\nconfig/dsn=""\noptions/auto_init=false\n', encoding='utf-8')
    (target / 'mods').mkdir(exist_ok=True)
    dep = target / 'mods/STS2-RitsuLib'
    shutil.copytree(RITSU, dep, dirs_exist_ok=True)
    return target


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', action='store_true', help='Export shared PCK and assemble/deploy the complete bundle.')
    parser.add_argument('--target', choices=['all', *GAME_TARGETS], default='all', help='A single target is a compile-only diagnostic.')
    args = parser.parse_args()
    if args.pack and args.target != 'all':
        parser.error('--pack requires both API targets')
    LOGS.mkdir(parents=True, exist_ok=True)
    declared = json.loads((PROJECT / 'Librarian.json').read_text(encoding='utf-8-sig'))
    if declared.get('version') != MOD_VERSION or declared.get('dependencies') != [{'id': 'STS2-RitsuLib', 'min_version': '0.6.4'}]:
        raise SystemExit('Unified build requires a consistent version and the Ritsu-only manifest')
    active = subprocess.run(['powershell', '-NoProfile', '-Command',
                             'Get-Process SlayTheSpire2,MegaDot*,Spine -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id; exit 0'],
                            capture_output=True, text=True)
    if active.stdout.strip():
        raise SystemExit('A shared game/editor process is active; finish it before build/deployment')
    env = isolated_environment(WORK / 'build-userdata')
    env.update(DOTNET_ROOT=str(SDK), PATH=str(SDK) + os.pathsep + env.get('PATH', ''),
               MSBUILDDISABLENODEREUSE='1', NUGET_PACKAGES=str(ROOT / '.research/nuget-packages'),
               DOTNET_CLI_HOME=str(WORK / 'build-userdata'))
    before = source_manifest()
    commands = []
    targets = GAME_TARGETS if args.target == 'all' else [args.target]
    for version in targets:
        target = WORK / 'build' / version
        command = [str(SDK / 'dotnet.exe'), 'build', str(PROJECT / 'Librarian.csproj'), '-c', 'Debug',
                   '--nologo', '-v', 'minimal', f'-p:LibrarianGameVersion={version}',
                   f'-p:OutputPath={target / "bin"}/', f'-p:BaseIntermediateOutputPath={target / "obj"}/']
        commands.append(run(command, env, f'content-{version}'))
    if args.target != 'all':
        print(f'PASS compile-only API target {args.target}; no complete package or deployment')
        return
    loader = WORK / 'build/loader'
    commands.append(run([str(SDK / 'dotnet.exe'), 'build', str(ROOT / 'src/Librarian.Loader/Librarian.Loader.csproj'),
                         '-c', 'Debug', '--nologo', '-v', 'minimal',
                         f'-p:LibrarianLoaderSdk={GAME_INPUTS["0.107.1"] / "data_sts2_windows_x86_64"}',
                         f'-p:OutputPath={loader / "bin"}/',
                         f'-p:BaseIntermediateOutputPath={loader / "obj"}/'], env, 'loader'))
    if not args.pack:
        print('PASS all DLLs compiled; --pack is required for a complete deployable bundle')
        return
    stage = WORK / 'package/Librarian'
    stage.mkdir(parents=True, exist_ok=True)
    export_cache = PROJECT / '.godot/mono/temp/bin/Debug'
    export_cache.mkdir(parents=True, exist_ok=True)
    editor_dependencies = [
        '0Harmony.dll', 'GodotSharp.dll', 'JetBrains.Annotations.dll',
        'MonoMod.Backports.dll', 'MonoMod.ILHelpers.dll', 'Sentry.dll', 'Sentry.Godot.dll',
        'SharpGen.Runtime.COM.dll', 'SharpGen.Runtime.dll', 'SmartFormat.dll',
        'SmartFormat.ZString.dll', 'Steamworks.NET.dll', 'sts2.dll',
        'Vortice.DirectX.dll', 'Vortice.DXGI.dll', 'Vortice.Mathematics.dll', 'WindowsBase.dll',
    ]
    for file in [*[GAME_INPUTS['0.111.0'] / 'data_sts2_windows_x86_64' / n for n in editor_dependencies],
                 *(RITSU / 'compat/0.111.0').glob('*.dll'), *(RITSU / 'shared').glob('*.dll'),
                 WORK / 'build/0.111.0/bin/Librarian.dll']:
        shutil.copy2(file, export_cache / file.name)
    # Register the game's immutable native extension only in the ignored editor cache.
    native = GAME_INPUTS['0.111.0'] / 'libspine_godot.windows.template_release.x86_64.dll'
    if hashlib.sha256(native.read_bytes()).hexdigest() != '854d827b8926b00ba6459093033bf0c0898efa2b6e1c85eb0abc78ca153ea58c':
        raise ValueError('Fixed private Spine extension changed.')
    extension = WORK / 'export-native/Spine.gdextension'
    extension.parent.mkdir(parents=True, exist_ok=True)
    extension.write_text('[configuration]\nentry_symbol="spine_godot_library_init"\n'
                         'compatibility_minimum="4.1"\n[libraries]\n' +
                         ''.join('windows.' + mode + '.x86_64="' + native.as_posix() + '"\n'
                                 for mode in ['editor', 'debug', 'release']), encoding='utf-8')
    def register_extension():
        (PROJECT / '.godot/extension_list.cfg').write_text(extension.as_posix() + '\n', encoding='utf-8')
    export_env = dict(env, LibrarianGameVersion='0.111.0')
    register_extension()
    commands.append(run([str(ROOT / '.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64_console.exe'),
                         '--headless', '--path', str(PROJECT), '--editor', '--quit-after', '30'],
                        export_env, 'native-editor-warmup', PROJECT))
    register_extension()
    commands.append(run([str(ROOT / '.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64_console.exe'),
                         '--headless', '--path', str(PROJECT), '--export-pack', 'BasicExport',
                         str(stage / 'Librarian.pck')], export_env, 'shared-pck', PROJECT))
    from repack_owned_png import add_raw_pages
    from inspect_owned_pck import catalog
    asset_root = 'res://Librarian/spine/v1.3.5-feedback'
    asset_dir = PROJECT / asset_root.removeprefix('res://')
    identity_pack = add_raw_pages(stage / 'Librarian.pck', asset_dir, asset_root,
                                  WORK / ('raw-pack-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')))
    packed = catalog(stage / 'Librarian.pck')
    entries = {r['path'].removeprefix('res://'): r for r in packed['files']}
    for file in asset_dir.iterdir():
        if file.is_file() and file.suffix in ('.res', '.spskel', '.spatlas', '.json', '.png'):
            resource = file.relative_to(PROJECT).as_posix()
            if resource not in entries or entries[resource]['sha256'] != hashlib.sha256(file.read_bytes()).hexdigest():
                raise ValueError('Native owned asset PCK bytes changed: ' + resource)
    forbidden = [name for name in entries if name.lower().endswith(('.dll', '.exe', '.gdextension', '.csproj', '.log'))
                 or name == '.godot/extension_list.cfg' or name == 'override.cfg' or '.research' in name]
    if forbidden:
        raise ValueError('Private/foreign files cannot enter the shared PCK: ' + repr(forbidden))
    for name, row in entries.items():
        if name.endswith('.cs') and (row['bytes'] > 1 or not (PROJECT / name).is_file()):
            raise ValueError('Only own engine script stubs may enter the PCK: ' + name)
    (WORK / 'logs/native-pck-catalog.json').write_text(json.dumps(packed, indent=2), encoding='utf-8')
    shutil.copy2(loader / 'bin/Librarian.Loader.dll', stage / 'Librarian.dll')
    shutil.copy2(PROJECT / 'Librarian.json', stage / 'Librarian.json')
    variants = []
    for version in GAME_TARGETS:
        relative = f'compat/{version}/Librarian.dll'
        destination = stage / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(WORK / f'build/{version}/bin/Librarian.dll', destination)
        variants.append({'game_version': version, 'assembly': 'Librarian', 'path': relative,
                         'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})
    (stage / 'librarian-variants.manifest').write_text(json.dumps({
        'schema_version': 1, 'mod_id': 'Librarian', 'mod_version': declared['version'], 'variants': variants},
        indent=2) + '\n', encoding='utf-8')
    after = source_manifest()
    identity = {'version': declared['version'], 'game_targets': list(GAME_TARGETS), 'commands': commands,
                'native_pack': identity_pack, 'provenance': {'source_before': before, 'source_after': after, 'environment': environment_manifest(SDK, env)}}
    if before['sha256'] != after['sha256']:
        (LOGS / 'input-drift.json').write_text(json.dumps(identity, indent=2), encoding='utf-8')
        raise SystemExit('Source changed during compilation/export; complete package was not promoted')
    identity['artifacts'] = validate_bundle(stage)
    CANDIDATE.mkdir(parents=True, exist_ok=True)
    shutil.copytree(stage, CANDIDATE, dirs_exist_ok=True)
    validate_bundle(CANDIDATE, identity['artifacts'])
    identity['deployments'] = []
    for version in GAME_TARGETS:
        runtime = prepare_runtime(version)
        destination = runtime / 'mods/Librarian'
        shutil.copytree(CANDIDATE, destination, dirs_exist_ok=True)
        validate_bundle(destination, identity['artifacts'])
        identity['deployments'].append({'game_version': version, 'runtime': str(runtime.relative_to(ROOT))})
    identity['candidate'] = str(CANDIDATE.relative_to(ROOT))
    (CANDIDATE.parents[1] / 'build-identity.json').write_text(json.dumps(identity, indent=2), encoding='utf-8')
    (LOGS / 'build-latest.json').write_text(json.dumps(identity, indent=2), encoding='utf-8')
    print(f'PASS complete bundle: {len(identity["artifacts"])} files; both isolated targets deployed')


if __name__ == '__main__':
    main()
