const test=require('node:test'),assert=require('node:assert/strict'),{parseXml,layout,options}=require('../electron/danmu.cjs'),{args,normalize}=require('../electron/player-config.cjs'),{MpvIpc}=require('../electron/mpv-ipc.cjs')
test('XML entities and injection are handled without activating ASS or external XML entities',()=>{
 const c=parseXml('<i><d p="1,1,25,0">&lt;测试&gt; &amp; &#x1f600; {\\blur50}</d></i>');assert.equal(c[0].text,'<测试> & 😀 {\\blur50}');assert.equal(c[0].color,0);const s=layout(c);assert(!s.items[0].text.includes('{'));assert(!s.items[0].text.includes('\\'));assert.throws(()=>parseXml('<!DOCTYPE i SYSTEM "file:///private"><i/>'))
})
test('invalid timestamps and unsupported advanced comments never enter the renderer',()=>{assert.equal(parseXml('<i><d p="NaN,1">bad</d><d p="-1,1">bad</d><d p="0,7">advanced</d><d p="2,4">bottom</d></i>').length,1)})
test('dense mixed comments do not overlap and respect region and density',()=>{
 const c=Array.from({length:4000},(_,i)=>({time:i*.013,mode:['scroll','top','bottom','reverse'][i%4],text:'不同宽度'+i+'x'.repeat(i%35),color:0xffffff})),s=layout(c,{region:.5,density:40});assert(s.displayed<c.length)
 for(let time=0;time<50;time+=.071){const active=s.items.filter(c=>c.t<=time&&c.t+c.d>time);assert(active.length<=40);for(const c of active)assert(c.y>=0&&c.y+s.fontSize<360)
  for(let i=0;i<active.length;i++)for(let j=i+1;j<active.length;j++){const a=active[i],b=active[j];if(a.y!==b.y)continue;const x=c=>c.m==='scroll'?1280-(1280+c.w)*(time-c.t)/c.d:c.m==='reverse'?-c.w+(1280+c.w)*(time-c.t)/c.d:640-c.w/2;assert(x(a)+a.w<=x(b)+.01||x(b)+b.w<=x(a)+.01,JSON.stringify({time,a,b}))}
 }
})
test('settings clamp malformed persisted data and keep video adjustments neutral',()=>{const s=normalize({fontSize:NaN,region:30,volume:1000,colorMode:'forced-hdr'});assert.equal(s.region,1);assert.equal(s.volume,100);assert.equal(s.colorMode,'auto');assert.equal(options({offset:Infinity}).offset,0);const a=args({pipe:'test',script:'cinema.lua',dataDir:'cache',settings:s});for(const setting of ['--vo=gpu-next','--hwdec=auto-safe','--d3d11va-zero-copy=no','--video-output-levels=auto','--target-colorspace-hint=auto','--deband=no','--interpolation=no'])assert(a.includes(setting));assert(!a.some(x=>/^--(vf|brightness|contrast|saturation|glsl-shaders|video-scale-x)=/.test(x)))})
test('IPC decodes split multibyte names and rejects pending commands on disconnect',async()=>{const p=new MpvIpc(),received=[];p.on('event',m=>received.push(m));const data=Buffer.from(JSON.stringify({event:'property-change',data:'第一集 😀'})+'\n');for(const byte of data)p.read(Buffer.from([byte]));assert.equal(received[0].data,'第一集 😀');p.socket={destroyed:false,write(){},destroy(){}};const pending=p.command(['get_property','pause']);p.close();await assert.rejects(pending,/结束/);assert.equal(p.pending.size,0)})
test('danmu refresh rate supports display-following and migrates the old 240 preset',()=>{
 for(const fps of [0,60,120])assert.equal(normalize({danmuFps:fps}).danmuFps,fps)
 assert.equal(normalize({danmuFps:240}).danmuFps,0)
 for(const fps of [null,'',30,144,Infinity,'invalid'])assert.equal(normalize({danmuFps:fps}).danmuFps,60)
 const a=args({pipe:'test',script:require('node:path').resolve('player/cinema.lua'),dataDir:'cache',settings:{danmuFps:240}})
 assert(a.includes('--osc=no'));assert(a.includes('--load-scripts=no'));assert(a.includes('--use-filedir-conf=no'));assert(a.includes('--load-select=no'));assert(a.some(s=>s.startsWith('--config-dir=')&&s.endsWith('modernz')));assert(a.some(s=>s.endsWith('modernz.lua')));assert(!a.some(s=>s.startsWith('--display-fps-override=')))
})
test('fractional danmu sizes survive parsing and keep large text inside the chosen region',()=>{
 assert.equal(options({fontSize:28.7}).fontSize,28.7);assert.equal(options({fontSize:10000}).fontSize,96)
 const scene=layout(Array.from({length:80},(_,i)=>({time:i*.3,mode:i%2?'top':'scroll',text:'连续字号 '+i,color:0xffffff})),{fontSize:95.9,region:.5})
 assert(scene.displayed>0);assert.equal(scene.fontSize,95.9);assert(scene.items.every(c=>c.y+scene.fontSize<scene.height*.5))
})
test('continuous durations, font weights and offline simplification reach the scene without changing XML',()=>{
 assert.equal(options({duration:20.7}).duration,20.7);assert.equal(options({duration:1}).duration,8);assert.equal(options({duration:100}).duration,32)
 const xml='<i><d p="0,1,25,16777215">這部動畫，後臺顯示彈幕。</d></i>',comments=parseXml(xml)
 const s=layout(comments,{duration:31.8,fontWeight:710});assert.equal(s.duration,31.8);assert.equal(s.items[0].d,31.8);assert.equal(s.fontWeight,710);assert.equal(s.items[0].text,'这部动画，后台显示弹幕。')
 assert.equal(comments[0].text,'這部動畫，後臺顯示彈幕。');assert.equal(layout(comments,{simplified:false}).items[0].text,comments[0].text);assert.equal(options({fontWeight:10000}).fontWeight,900)
})

test('player UI weight is independent and bounded',()=>{
 assert.equal(normalize({uiFontWeight:650,fontWeight:300}).uiFontWeight,650);assert.equal(normalize({uiFontWeight:650,fontWeight:300}).fontWeight,300)
 assert.equal(normalize({uiFontWeight:9999}).uiFontWeight,900);assert.equal(normalize({uiFontWeight:NaN}).uiFontWeight,400)
})
test('player UI font size and refresh rate normalize independently from danmu and subtitle settings',()=>{for(const value of [12,16,20.5,24])assert.equal(normalize({uiFontSize:value}).uiFontSize,value);assert.equal(normalize({uiFontSize:200}).uiFontSize,24);assert.equal(normalize({uiFontSize:NaN}).uiFontSize,16);for(const value of [0,60,120,240])assert.equal(normalize({uiFps:value}).uiFps,value);for(const value of [30,144,Infinity])assert.equal(normalize({uiFps:value}).uiFps,0);const s=normalize({uiFontSize:22,fontSize:35,uiFontWeight:700,fontWeight:300});assert.equal(s.fontSize,35);assert.equal(s.fontWeight,300);assert.equal(s.uiFontWeight,700)})

test('new playback opens fullscreen while F remains a normal toggle',()=>{const a=args({pipe:'test',script:'cinema.lua',dataDir:'cache',settings:{}});assert(a.includes('--fullscreen=yes'))})

test('preload budget bounds memory without changing video quality or claiming GPU utilization',()=>{
 const {preloadArgs}=require('../electron/player-config.cjs')
 for(const value of [null,'',NaN,Infinity,'invalid'])assert.equal(normalize({preloadBudget:value}).preloadBudget,25)
 assert.equal(normalize({preloadBudget:-1}).preloadBudget,0);assert.equal(normalize({preloadBudget:120}).preloadBudget,100)
 assert.deepEqual(preloadArgs({preloadBudget:0}),['--demuxer-max-bytes=64MiB','--demuxer-max-back-bytes=16MiB'])
 assert.deepEqual(preloadArgs({preloadBudget:100}),['--demuxer-max-bytes=512MiB','--demuxer-max-back-bytes=256MiB'])
 const low=args({pipe:'test',script:'cinema.lua',dataDir:'cache',settings:{preloadBudget:0}}),high=args({pipe:'test',script:'cinema.lua',dataDir:'cache',settings:{preloadBudget:100}})
 assert.deepEqual(low.filter(a=>!a.startsWith('--demuxer-max-')),high.filter(a=>!a.startsWith('--demuxer-max-')))
})

test('renderer prefers native D3D11 with a Vulkan fallback and preserves neutral video processing',()=>{
 const a=args({pipe:'test',script:'cinema.lua',dataDir:'cache',settings:{}})
 assert(a.includes('--gpu-api=d3d11,vulkan'));assert(a.includes('--gpu-context=d3d11,winvk'));assert(a.includes('--target-colorspace-hint=auto'));assert(a.includes('--interpolation=no'));assert(a.includes('--hwdec=auto-safe'))
})
