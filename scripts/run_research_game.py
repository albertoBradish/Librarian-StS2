"""Launch only isolated research processes; no original-game deployment or save writes."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import ctypes
import shutil
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / '.research/recovered-work'
ENGINE = ROOT / '.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64.exe'
USER = ROOT / '.research/runtime-userdata'
LOGS = ROOT / '.research/logs'

def isolated_environment(user_root=USER):
    env = os.environ.copy()
    for key, sub in [('APPDATA', 'appdata'), ('LOCALAPPDATA', 'localappdata'),
                     ('TEMP', 'temp'), ('TMP', 'temp')]:
        folder = user_root / sub
        folder.mkdir(parents=True, exist_ok=True)
        env[key] = str(folder)
    env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    return env

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['path-probe', 'import', 'launch', 'compatibility'])
    parser.add_argument('--headless-check', action='store_true', help='Start a bounded noninteractive loading check.')
    parser.add_argument('--model-audit', action='store_true', help='Audit Librarian models at the main menu without starting a run.')
    parser.add_argument('--runtime-audit', action='store_true', help='Run the opt-in revision-specific audit in a disposable compatibility profile.')
    parser.add_argument('--choice-audit', action='store_true', help='After runtime checks, open three native choice screens for interactive validation.')
    parser.add_argument('--legacy-save-audit', action='store_true', help='Load a read-only copy of the previous isolated tester run with the new mod.')
    parser.add_argument('--headless-frames', type=int, default=1800, help='Frame budget for a bounded loading check.')
    args = parser.parse_args()
    LOGS.mkdir(parents=True, exist_ok=True)
    engine = ENGINE if args.mode != 'compatibility' else ROOT / '.research/ritsu-game/SlayTheSpire2.exe'
    if args.mode in ['launch', 'compatibility']:
        previous = LOGS / f'{args.mode}-latest.json'
        if previous.exists():
            info = json.loads(previous.read_text(encoding='utf-8'))
            kernel = ctypes.WinDLL('kernel32', use_last_error=True)
            kernel.OpenProcess.restype = ctypes.c_void_p
            handle = kernel.OpenProcess(0x1000, False, info['pid'])
            if handle:
                try:
                    path = ctypes.create_unicode_buffer(32768)
                    size = ctypes.c_ulong(len(path))
                    if kernel.QueryFullProcessImageNameW(ctypes.c_void_p(handle), 0, path, ctypes.byref(size)):
                        if Path(path.value).resolve() == engine.resolve():
                            print('The research game is already running. Use its existing window.')
                            return
                finally:
                    kernel.CloseHandle(ctypes.c_void_p(handle))
    user_root = USER if args.mode != 'compatibility' else ROOT / '.research/ritsu-userdata'
    if args.runtime_audit:
        if args.mode != 'compatibility':
            raise SystemExit('Runtime audit requires the isolated compatibility game')
        user_root = ROOT / '.research/ritsu-only/revision030-userdata'
        source_profile = ROOT / '.research/ritsu-userdata/appdata/Sts2Compatibility-v0.111.0/default'
        target_profile = user_root / 'appdata/Sts2Compatibility-v0.111.0/default'
        if not target_profile.exists():
            shutil.copytree(source_profile, target_profile)
    env = isolated_environment(user_root)
    if args.runtime_audit:
        env['LIBRARIAN_RUNTIME_AUDIT'] = '1'
    if args.choice_audit:
        if not args.runtime_audit or args.headless_check:
            raise SystemExit('Choice audit requires a visible isolated runtime audit')
        env['LIBRARIAN_CHOICE_AUDIT'] = '1'
    if args.legacy_save_audit:
        if not args.runtime_audit or args.choice_audit:
            raise SystemExit('Legacy save audit requires runtime-audit without choice-audit')
        relative = Path('appdata/Sts2Compatibility-v0.111.0/default/1/modded/profile1/saves/current_run.save')
        source = ROOT / '.research/ritsu-userdata' / relative
        destination = user_root / relative
        if destination.exists():
            shutil.copy2(destination, destination.with_name('current_run.before-legacy-audit.save'))
        shutil.copy2(source, destination)
        env['LIBRARIAN_LEGACY_SAVE_AUDIT'] = '1'
    project = WORK if args.mode != 'compatibility' else engine.parent
    expected_assembly = WORK / '.godot/mono/temp/bin/Debug/sts2.dll' if args.mode != 'compatibility' else project / 'data_sts2_windows_x86_64/sts2.dll'
    env['RESEARCH_EXPECTED_ASSEMBLY'] = str(expected_assembly)
    if args.model_audit:
        if args.mode != 'compatibility':
            raise SystemExit('Model audits require the original-binary compatibility copy.')
        env['LIBRARIAN_MODEL_AUDIT'] = '1'
    command = [str(engine), '--path', str(project)]
    if args.mode == 'path-probe':
        project = ROOT / '.research/path-probe'
        project.mkdir(exist_ok=True)
        (project / 'project.godot').write_text('config_version=5\n[application]\nconfig/name="ResearchPathProbe"\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="Sts2Research-v0.111.0"\n', encoding='utf-8')
        (project / 'probe.gd').write_text('extends SceneTree\nfunc _initialize():\n\tprint("RESEARCH_USER_DIR=" + OS.get_user_data_dir())\n\tprint("RESEARCH_GLOBAL_USER=" + ProjectSettings.globalize_path("user://"))\n\tquit()\n', encoding='utf-8')
        command = [str(ENGINE), '--headless', '--path', str(project), '--script', 'probe.gd']
    elif args.mode == 'import':
        command += ['--headless', '--editor', '--import']
    else:
        command += ['--force-steam', 'off']
        if args.headless_check:
            command += ['--headless', '--max-fps', '60', '--quit-after', str(args.headless_frames)]
        else:
            command += ['--windowed', '--resolution', '1280x720']
        if args.mode != 'compatibility':
            command += ['--nomods']
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    logfile = LOGS / f'{args.mode}-{stamp}.log'
    with logfile.open('wb') as out:
        process = subprocess.Popen(command, cwd=project, env=env, stdout=out,
                                   stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
    info = {'mode': args.mode, 'pid': process.pid, 'command': command,
            'log': str(logfile), 'started_utc': stamp,
            'APPDATA': env['APPDATA'], 'LOCALAPPDATA': env['LOCALAPPDATA']}
    (LOGS / f'{args.mode}-latest.json').write_text(json.dumps(info, indent=2), encoding='utf-8')
    print(json.dumps(info, indent=2))
    if args.mode == 'path-probe':
        code = process.wait(timeout=30)
        content = logfile.read_text(encoding='utf-8', errors='replace')
        print(content)
        expected = str(USER / 'appdata/Sts2Research-v0.111.0').replace('\\', '/')
        if code or f'RESEARCH_USER_DIR={expected}' not in content.replace('\\', '/'):
            raise SystemExit('User-data isolation probe failed; do not launch the game.')
        print('PASS: engine user:// resolves inside the research workspace')

if __name__ == '__main__':
    main()
