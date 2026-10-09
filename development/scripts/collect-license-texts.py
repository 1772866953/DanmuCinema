"""Collect license and copyright texts from the offline component sources."""
import json
import re
import sys
import tarfile
from pathlib import Path

source, output = map(Path, sys.argv[1:3])
pattern = re.compile(r"^(LICENSE|LICENCE|COPYING|NOTICE|NOTICES|COPYRIGHT)(?:$|[._-])", re.I)


def save(relative, parts):
    if not parts:
        raise RuntimeError(f"No license text found for {relative}")
    dest = output / relative / "LICENSE.txt"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text("\n\n".join(parts) + "\n", encoding="utf-8")


save("DotNet", [(source / name).read_text(encoding="utf-8-sig")
                 for name in ("LICENSE-DotNet.txt", "NOTICES-DotNet.txt")])
notices = source / "runtime-notices"
metadata = json.loads((notices / "packages.json").read_text(encoding="utf-8-sig"))
copyrights = {item["package"].lower().replace("/", "-"): item.get("copyright", "")
              for item in metadata}
for directory in sorted(p for p in notices.iterdir() if p.is_dir()):
    parts = []
    copyright_text = copyrights.get(directory.name.lower(), "")
    if copyright_text:
        parts.append(copyright_text)
    for file in sorted(directory.rglob("*")):
        if file.is_file() and file.suffix.lower() not in ('.nuspec', '.nupkg', '.json'):
            parts.append(file.name + "\n\n" + file.read_text(encoding="utf-8-sig", errors="replace"))
    save("NuGet/" + directory.name, parts)

archives = sorted(source.glob("*.tar.gz")) + sorted((source / "ffmpeg-dependencies").iterdir())
count = 0
for archive in archives:
    if not archive.is_file() or not tarfile.is_tarfile(archive):
        continue
    parts = []
    with tarfile.open(archive) as tar:
        for member in tar:
            name = Path(member.name).name
            if member.isfile() and pattern.match(name) and member.size < 4 * 1024 * 1024:
                body = tar.extractfile(member).read().decode("utf-8-sig", errors="replace")
                parts.append(member.name + "\n\n" + body)
    # Some dependency archives put their license in source headers instead.
    # Their original complete archives are also distributed in the source package.
    if parts:
        save("Components/" + archive.name.removesuffix(".tar.gz").removesuffix(".tar.xz").removesuffix(".tar.bz2"), parts)
        count += 1
print(f"Collected {count} source license groups and NuGet/.NET licenses.")
