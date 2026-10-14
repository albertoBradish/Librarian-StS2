"""Read-only standalone PCK2/3 catalog and payload verification.

Format checked against official Godot 4.5.1 file_access_pack.cpp/.h:
https://github.com/godotengine/godot/blob/4.5.1-stable/core/io/file_access_pack.cpp
No native source or payload is copied by this reader.
"""
from pathlib import Path,PurePosixPath
import hashlib,json,argparse

def catalog(path):
    data=path.read_bytes();cursor=0
    def take(length):
        nonlocal cursor
        if length<0 or cursor+length>len(data):raise ValueError('Truncated standalone PCK.')
        result=data[cursor:cursor+length];cursor+=length;return result
    def integer(size):return int.from_bytes(take(size),'little')
    if take(4)!=b'GDPC':raise ValueError('Require a standalone PCK, not an embedded executable.')
    version=integer(4);engine=[integer(4) for _ in range(3)];flags=integer(4);base=integer(8)
    if version not in [2,3] or flags&5:raise ValueError('Unsupported/encrypted/sparse package.')
    if version==3:cursor=integer(8)
    else:take(64)
    count=integer(4)
    if not 0<count<100000:raise ValueError('Invalid PCK entry count.')
    rows=[];seen=set()
    for _ in range(count):
        length=integer(4)
        if length>16384:raise ValueError('Invalid path length.')
        name=take(length).rstrip(b'\0').decode('utf-8')
        offset=base+integer(8);size=integer(8);md5=take(16);entry_flags=integer(4)
        pure=PurePosixPath(name.removeprefix('res://'))
        if name in seen or pure.is_absolute() or '..' in pure.parts or '\\' in name or ':' in str(pure):raise ValueError('Invalid/duplicate PCK path.')
        if entry_flags or offset+size>len(data):raise ValueError('Encrypted/removal/truncated PCK payload.')
        payload=data[offset:offset+size]
        if hashlib.md5(payload).digest()!=md5:raise ValueError('PCK payload MD5 mismatch: '+name)
        seen.add(name);rows.append({'path':name,'offset':offset,'bytes':size,'sha256':hashlib.sha256(payload).hexdigest()})
    return {'format_version':version,'engine':engine,'flags':flags,'files':rows,'all_payload_MD5_pass':True,
            'pck_sha256':hashlib.sha256(data).hexdigest()}

def check_owned(result,asset_root):
    names=[r['path'].removeprefix('res://') for r in result['files']]
    forbidden=[n for n in names if n.lower().endswith(('.dll','.exe','.gdextension','.cs','.csproj','.log'))
               or n=='.godot/extension_list.cfg' or '.research' in n or n.endswith('create_bank.gd') or n.endswith('read_bank.gd')]
    required=[asset_root+'/'+n for n in ['bank.res','atlas.spatlas','skeleton-file.spskel','runtime-receipt.json']]
    missing=[n for n in required if n not in names]
    outside=[n for n in names if not n.startswith(asset_root+'/') and not n.startswith('.godot/') and n not in ['project.binary','project.godot']]
    return {'forbidden':forbidden,'missing':missing,'outside_owned_bank_or_engine_cache':outside,
            'passed':not(forbidden or missing or outside)}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('pck');parser.add_argument('--asset-root');parser.add_argument('--output',required=True)
    args=parser.parse_args();path=Path(args.pck).resolve();result=catalog(path)
    if args.asset_root:result['ownership']=check_owned(result,args.asset_root.removeprefix('res://').rstrip('/'))
    output=Path(args.output)
    if output.exists():raise RuntimeError('Retain previous catalog evidence.')
    output.write_text(json.dumps(result,indent=2)+'\n','utf-8')
    print(json.dumps({'PCK_payloads':len(result['files']),'engine':result['engine'],'payload_MD5':True,'ownership':result.get('ownership')}))
    if args.asset_root and not result['ownership']['passed']:raise SystemExit(1)
