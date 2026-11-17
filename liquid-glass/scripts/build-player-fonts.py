"""Build static UI faces from the bundled OFL variable font for libass.
Development only: python -m pip install --target development/dependencies/fonttools fonttools==4.60.1
"""
from pathlib import Path
import sys,hashlib,json
ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'development/dependencies/fonttools'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
source=ROOT/'public/fonts/noto-sans-sc.ttf'
out=ROOT/'player/ui-fonts';out.mkdir(exist_ok=True)
styles=['Thin','ExtraLight','Light','Regular','Medium','SemiBold','Bold','ExtraBold','Black']
for weight,style in zip(range(100,901,100),styles):
    target=out/f'cinema-ui-{weight}.ttf'
    if target.exists(): continue
    font=instantiateVariableFont(TTFont(source),{'wght':weight},inplace=True,optimize=False)
    names=font['name']
    for name in list(names.names):
        if name.nameID in (1,2,3,4,6,16,17):names.names.remove(name)
    for platform,encoding,language in [(3,1,0x409),(0,3,0)]:
        for nameid,value in {1:'DanmuCinema UI',2:style,3:f'DanmuCinemaUI-{style}',4:f'DanmuCinema UI {style}',6:f'DanmuCinemaUI-{style}',16:'DanmuCinema UI',17:style}.items():names.setName(value,nameid,platform,encoding,language)
    font['OS/2'].usWeightClass=weight
    font['OS/2'].fsSelection=(font['OS/2'].fsSelection & ~0x61) | (0x20 if weight>=700 else 0x40 if weight==400 else 0)
    font['head'].macStyle=1 if weight>=700 else 0
    font.save(target);font.close();print(f'{target.name}: {target.stat().st_size}',flush=True)
(out/'build.json').write_text(json.dumps({'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'fonttools':'4.60.1','weights':list(range(100,901,100))},indent=2)+'\n',encoding='utf-8')

faces={}
for weight in range(100,901,100):
    font=TTFont(out/f'cinema-ui-{weight}.ttf');units=font['head'].unitsPerEm
    faces[str(weight)]={str(cp):advance for cp,glyph in font.getBestCmap().items() if (advance:=font['hmtx'][glyph][0])!=units}
    font.close()
(ROOT/'player/ui-metrics.json').write_text(json.dumps({'units':units,'faces':faces},separators=(',',':'))+'\n',encoding='utf-8')
