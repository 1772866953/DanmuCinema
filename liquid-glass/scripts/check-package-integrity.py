import hashlib
import json
import os
from pathlib import Path
import struct
import sys

root = Path(__file__).resolve().parent.parent
version = sys.argv[1] if len(sys.argv) > 1 else json.loads((root / 'package.json').read_text())['version']
package = root.parent / 'packages' / version
stage = Path(os.environ['TEMP']) / f'DanmuCinema-package-{version}' / 'win-unpacked'
digest = lambda value: hashlib.sha256(value).hexdigest()

def icon_payloads(file):
    data = file.read_bytes()
    u16 = lambda at: struct.unpack_from('<H', data, at)[0]
    u32 = lambda at: struct.unpack_from('<I', data, at)[0]
    nt = u32(0x3c)
    optional = nt + 24
    sections = optional + u16(nt + 20)
    def offset(rva):
        for i in range(u16(nt + 6)):
            section = sections + 40 * i
            address = u32(section + 12)
            if address <= rva < address + max(u32(section + 8), u32(section + 16)):
                return u32(section + 20) + rva - address
        raise ValueError('Unmapped PE resource RVA')
    resource = offset(u32(optional + (112 if u16(optional) == 0x20b else 96) + 16))
    result = set()
    def visit(relative, kinds):
        directory = resource + relative
        for i in range(u16(directory + 12) + u16(directory + 14)):
            entry = directory + 16 + 8 * i
            name, target = u32(entry), u32(entry + 4)
            path = kinds + [name]
            if target & 0x80000000:
                visit(target & 0x7fffffff, path)
            elif path[0] == 3:
                payload = resource + target
                start, size = offset(u32(payload)), u32(payload + 4)
                result.add(digest(data[start:start + size]))
    visit(0, [])
    return result

source_icon = (root / 'public' / 'icon.ico').read_bytes()
expected = set()
for i in range(struct.unpack_from('<H', source_icon, 4)[0]):
    size, start = struct.unpack_from('<II', source_icon, 6 + 16 * i + 8)
    expected.add(digest(source_icon[start:start + size]))
installer = package / f'DanmuCinema-Setup-{version}-win-x64.exe'
report = {'version': version, 'checksums': [], 'icons': [], 'passed': False}
for file in [installer, package / f'DanmuCinema-Corresponding-Sources-{version}.zip']:
    actual = digest(file.read_bytes())
    stated, name = Path(str(file) + '.sha256').read_text().strip().split('  ', 1)
    assert actual.lower() == stated.lower() and name == file.name, 'Checksum mismatch: ' + file.name
    report['checksums'].append({'file': file.name, 'sha256': actual, 'bytes': file.stat().st_size})
for file in [installer, stage / 'DanmuCinema.exe', stage / 'resources' / 'engine' / 'runtime' / 'mpv' / 'mpv.exe']:
    present = icon_payloads(file)
    assert expected.issubset(present), 'Application icon frames missing: ' + file.name
    report['icons'].append({'file': file.name, 'matchingFrames': len(expected)})
report['passed'] = True
(root / 'artifacts' / f'package-integrity-{version}.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
