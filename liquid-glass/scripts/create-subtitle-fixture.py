"""Create generated ASS/embedded-font test assets inside artifacts only."""
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'development/dependencies/fonttools'))
from fontTools.ttLib import TTFont
target=Path(sys.argv[1]).resolve()
assert target.is_relative_to(ROOT/'artifacts')
target.mkdir(parents=True,exist_ok=True)
font=TTFont(ROOT/'runtime/mpv/fonts/cinema-ui-400.ttf')
names=font['name']
for entry in list(names.names):
    if entry.nameID in (1,2,3,4,6,16,17):names.names.remove(entry)
for platform,encoding,language in [(3,1,0x409),(0,3,0)]:
    for nameid,value in {1:'Fixture Embedded',2:'Regular',3:'FixtureEmbedded-Regular',4:'Fixture Embedded Regular',6:'FixtureEmbedded-Regular',16:'Fixture Embedded',17:'Regular'}.items():names.setName(value,nameid,platform,encoding,language)
font.save(target/'fixture.ttf');font.close()
(target/'styled.ass').write_text(r'''[Script Info]
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 720
ScaledBorderAndShadow: yes
YCbCr Matrix: TV.709
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Fixture Embedded,46,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,3,2,2,24,24,35,1
Style: Sign,Fixture Embedded,32,&H0000FFFF,&H000000FF,&H00332211,&H80000000,-1,-1,0,0,100,100,0,0,1,2,1,7,15,15,15,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:08.00,Default,,0,0,0,,原样式：字幕字体和描边 Native subtitle
Dialogue: 1,0:00:00.00,0:00:08.00,Sign,,0,0,0,,{\pos(80,45)\c&H00FFFF&}左上：黄色斜体 Top left
Dialogue: 2,0:00:00.00,0:00:08.00,Default,,0,0,0,,{\an9\pos(1210,340)\fs30\c&H00FF00&}右侧定位\N第二行
Dialogue: 3,0:00:00.00,0:00:08.00,Default,,0,0,0,,{\an7\move(160,170,850,220,0,4000)\c&HFF9933&\t(0,4000,\frz20\fscx140)\fad(100,300)}移动与旋转 Move
Dialogue: 4,0:00:00.00,0:00:08.00,Default,,0,0,0,,{\an7\pos(180,420)\p1\bord0\shad0\c&H3366FF&\clip(180,420,300,480)}m 0 0 l 180 0 l 180 90 l 0 90
Dialogue: 5,0:00:00.00,0:00:08.00,Default,,0,0,0,,{\an7\pos(560,420)\fs36\k100}高{\k100}级{\k100}字{\k100}幕
''',encoding='utf-8')
(target/'plain.srt').write_text('1\n00:00:00,000 --> 00:00:08,000\n普通文字字幕 Plain text\n',encoding='utf-8')
