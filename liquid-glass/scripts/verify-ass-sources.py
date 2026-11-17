"""Verify upstream source/binary identity and collect original license texts."""
from pathlib import Path, PurePosixPath
import hashlib,json,re,tarfile,zipfile

ROOT=Path(__file__).resolve().parent.parent
SOURCES=ROOT/'development/dependencies/jassub-sources'
PACKAGE=ROOT/'node_modules/jassub'
if not PACKAGE.exists(): PACKAGE=ROOT/'development/dependencies/ass-overlay/node_modules/jassub'
PINS={
    'jassub':'4c854810063f24adab4b5085027f30f776016103',
    'brotli':'028fb5a23661f123017c060daa546b55cf4bde29',
    'freetype':'0a0221a1347e2f1e07c395263540026e9a0aa7c7',
    'fribidi':'68162babff4f39c4e2dc164a5e825af93bda9983',
    'harfbuzz':'30485ee8c3d43c553afb9d78b9924cb71c8d2f19',
    'libass':'fbe73bf18bd98e3ba14ce6d5e4bd1ba202a25ef4',
}
REPOS={'jassub':'ThaUnknown/jassub','brotli':'google/brotli','freetype':'freetype/freetype','fribidi':'fribidi/fribidi','harfbuzz':'harfbuzz/harfbuzz','libass':'libass/libass'}
def sha(data):return hashlib.sha256(data).hexdigest()
licenses={}
manifest={'version':'2.5.37','sources':[],'wasm':{},'typescriptFilesVerified':0}
for name,pin in PINS.items():
    archive=SOURCES/(name+'.tar.gz')
    with tarfile.open(archive) as source:
        commit=source.pax_headers.get('comment','')
        if not re.fullmatch(r'[a-f0-9]{40}',commit) or not commit.startswith(pin):raise ValueError('Unexpected source commit: '+name)
        files={m.name:m for m in source.getmembers() if m.isfile()}
        prefix=next(iter(files)).split('/')[0]+'/'
        if name=='jassub':
            version=json.loads(source.extractfile(prefix+'package.json').read())['version']
            if version!=manifest['version'] or json.loads((PACKAGE/'package.json').read_text())['version']!=version:raise ValueError('JASSUB version changed')
            for binary in ['jassub-worker.wasm','jassub-worker-modern.wasm']:
                upstream=source.extractfile(prefix+'src/wasm/'+binary).read()
                installed=(PACKAGE/'dist/wasm'/binary).read_bytes()
                if upstream!=installed:raise ValueError('WASM does not match corresponding source: '+binary)
                manifest['wasm'][binary]=sha(installed)
            for script in PACKAGE.glob('src/**/*.ts'):
                relative=script.relative_to(PACKAGE).as_posix()
                if source.extractfile(prefix+relative).read().replace(b'\r',b'')!=script.read_bytes().replace(b'\r',b''):raise ValueError('TypeScript source changed: '+relative)
                manifest['typescriptFilesVerified']+=1
            licenses['LICENSE-JASSUB']=source.extractfile(prefix+'LICENSE').read()
            licenses['LICENSE-JASSUB-Dependencies']=source.extractfile(prefix+'build/license_fullnotice').read()
        else:
            found=0
            for path,member in files.items():
                relative=path[len(prefix):]
                filename=PurePosixPath(relative).name
                if not re.fullmatch(r'(?:LICEN[CS]E(?:[._-].*)?|COPYING(?:[._-].*)?|COPYRIGHT(?:[._-].*)?|FTL\.TXT|GPLv2\.TXT|OFL\.TXT)',filename,re.I):continue
                text=source.extractfile(member).read()
                try:text.decode('utf8')
                except UnicodeDecodeError:continue
                if b'\x00' in text:continue
                target='LICENSE-JASSUB-'+name+'-'+re.sub(r'[^\w.-]','_',relative)
                licenses[target]=text;found+=1
            if not found:raise ValueError('No upstream license found: '+name)
        manifest['sources'].append({'name':name,'repo':REPOS[name],'commit':commit,'file':archive.name,'sha256':sha(archive.read_bytes())})

font=(SOURCES/'LICENSE-LiberationSans').read_bytes()
if b'2010 Google Corporation' not in font or b'Reserved Font Name Liberation' not in font or b'SIL OPEN FONT LICENSE Version 1.1' not in font:raise ValueError('Unexpected bundled font license')
licenses['LICENSE-JASSUB-LiberationSans']=font
apache=(SOURCES/'LICENSE-Apache-2.0').read_bytes()
if b'Apache License' not in apache or b'Version 2.0, January 2004' not in apache:raise ValueError('Unexpected Apache license')
mit=licenses['LICENSE-JASSUB'];mit=mit[mit.index(b'Permission is hereby granted'):]
manifest['javascriptDependencies']=[]
with zipfile.ZipFile(SOURCES/'javascript-dependencies.zip','w',zipfile.ZIP_DEFLATED) as bundled:
    for name,expected,license_name in [('abslink','1.3.1','Apache-2.0'),('lfa-ponyfill','1.1.1','MIT'),('throughput','1.0.2','MIT')]:
        directory=PACKAGE.parent/name;meta=json.loads((directory/'package.json').read_text())
        if meta['version']!=expected or meta['license']!=license_name:raise ValueError('JavaScript dependency changed: '+name)
        notice=f"{name} {expected}\nProject: {meta['repository']['url']}\nDeclared license: {license_name}\n"
        if meta.get('author'):notice+='Author (package metadata): '+str(meta['author'])+'\n'
        licenses['LICENSE-JASSUB-'+name]=notice.encode()+b'\n'+(apache if license_name=='Apache-2.0' else mit)
        for path in sorted(directory.rglob('*')):
            if not path.is_file() or path.name.lower().startswith('readme'):continue
            entry=zipfile.ZipInfo(name+'/'+path.relative_to(directory).as_posix(),date_time=(1980,1,1,0,0,0));entry.compress_type=zipfile.ZIP_DEFLATED
            bundled.writestr(entry,path.read_bytes())
        manifest['javascriptDependencies'].append({'name':name,'version':expected,'license':license_name})
manifest['javascriptArchiveSha256']=sha((SOURCES/'javascript-dependencies.zip').read_bytes())
license_root=ROOT/'licenses'
for name,content in licenses.items():
    # Normalize transport CRLFs; do not rewrite the upstream license wording.
    content=content.replace(b'\r\r\n',b'\n').replace(b'\r\n',b'\n')
    (license_root/name).write_bytes(content)
manifest['licenses']=sorted(licenses)
(SOURCES/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print(json.dumps({'passed':True,'sources':len(manifest['sources']),'wasmBinaries':len(manifest['wasm']),'typescriptFiles':manifest['typescriptFilesVerified'],'licenses':len(licenses)}))
