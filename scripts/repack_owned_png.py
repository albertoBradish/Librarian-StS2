"""Retain every exported payload byte and add only owned raw atlas PNG pages."""
from pathlib import Path
import hashlib,json,shutil,subprocess
from inspect_owned_pck import catalog
ROOT=Path(__file__).resolve().parents[1]
ENGINE=ROOT/'.research/tools/megadot/MegaDot_v4.5.1-stable_mono_win64_console.exe'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def add_raw_pages(pack,asset_dir,asset_root,work):
    work.mkdir(exist_ok=False)
    previous=work/'export-original.pck'
    shutil.copy2(pack,previous)
    inspected=catalog(previous);data=previous.read_bytes();files=[]
    for row in inspected['files']:
        name=row['path'].removeprefix('res://')
        target=work/'payloads'/name
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(data[row['offset']:row['offset']+row['bytes']])
        assert sha(target)==row['sha256']
        files.append({'source':target.as_posix(),'resource_path':'res://'+name,'sha256':row['sha256']})
    raw_pages=[]
    for page in sorted(asset_dir.glob('*.png')):
        name=asset_root+'/'+page.name
        assert name not in {x['resource_path'] for x in files}
        row={'source':page.as_posix(),'resource_path':name,'sha256':sha(page)}
        raw_pages.append(row);files.append(row)
    assert len(raw_pages)==2
    host=work/'host';host.mkdir()
    (host/'project.godot').write_text('config_version=5\n[application]\nconfig/name="Own exact payload packer"\n','utf-8')
    shutil.copy2(Path(__file__).parent/'pack_exact_payloads.gd.in',host/'pack.gd')
    output=work/'exact-owned.pck';config=work/'config.json'
    config.write_text(json.dumps({'output':output.as_posix(),'files':files},indent=2),'utf-8')
    cmd=[str(ENGINE),'--headless','--path',str(host),'--script','pack.gd','--',str(config)]
    log=work/'native-pack.log'
    with log.open('xb') as stream:
        result=subprocess.run(cmd,stdout=stream,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW,timeout=120)
    text=log.read_text('utf-8-sig',errors='replace')
    assert result.returncode==0 and 'ERROR:' not in text and 'OWNED_EXACT_PAYLOAD_PACK_PASS' in text,text
    actual=catalog(output)
    actual_map={x['path'].removeprefix('res://'):x['sha256'] for x in actual['files']}
    assert actual_map=={x['resource_path'].removeprefix('res://'):x['sha256'] for x in files}
    shutil.copy2(output,pack)
    report={'command':cmd,'exit_code':result.returncode,'original_export_sha256':sha(previous),'exact_final_sha256':sha(pack),'all_original_payloads_byte_identical':True,'original_count':len(inspected['files']),'raw_pages_added':raw_pages,'all_final_payload_MD5_pass':actual['all_payload_MD5_pass']}
    (work/'verification.json').write_text(json.dumps(report,indent=2)+'\n','utf-8')
    return report
