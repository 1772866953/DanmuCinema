// Isolated feasibility test. Does not load the installed app or change its files.
import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {createRequire} from 'node:module'
import {build} from 'vite'
import http from 'node:http'
import {execFile} from 'node:child_process'
import {promisify} from 'node:util'
import {runtime} from './test-runtime.mjs'
const require=createRequire(import.meta.url),{layout,parseXml}=require('../electron/danmu.cjs')
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/independent-overlay-'+Date.now())
await fs.mkdir(out,{recursive:true})
const web=path.join(out,'web')
await build({configFile:false,root:process.cwd(),base:'/',build:{outDir:web,emptyOutDir:true,rollupOptions:{input:path.resolve('development/experiments/ass-overlay/index.html')}}})
const server=http.createServer(async(req,res)=>{try{const relative=decodeURIComponent(new URL(req.url,'http://localhost').pathname).replace(/^\//,''),file=path.resolve(relative.startsWith('runtime/')?process.cwd():web,relative);if(!file.startsWith(web+path.sep)&&!file.startsWith(path.resolve('runtime/mpv/fonts')+path.sep))throw Error('outside test assets');res.setHeader('Cross-Origin-Embedder-Policy','require-corp');res.setHeader('Cross-Origin-Opener-Policy','same-origin');res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.wasm':'application/wasm','.ttf':'font/ttf'})[path.extname(file)]||'application/octet-stream');res.end(await fs.readFile(file))}catch(e){res.writeHead(404);res.end(String(e))}})
await new Promise(r=>server.listen(0,'127.0.0.1',r))
const url=`http://127.0.0.1:${server.address().port}/development/experiments/ass-overlay/index.html`
const videoTest=process.env.DANMU_TEST_VIDEO==='1',scripts=path.join(out,'player')
if(videoTest){
 await fs.cp(path.resolve('player'),scripts,{recursive:true})
 let source=await fs.readFile(path.join(scripts,'cinema.lua'),'utf8')
 source=source.replace("local active = scene and enabled and #scene.items > 0 and not minimized","local active = false").replace(/local sync = not changing_speed[^\r\n]+/,"local sync = 'audio'")
 await fs.writeFile(path.join(scripts,'cinema.lua'),source)
 await fs.writeFile(path.join(scripts,'cinema-osd.lua'),`local mp,utils=require 'mp',require 'mp.utils';local M={};local serial=0;function M.create(format)serial=serial+1;local id=mp.get_script_name()..':'..serial;local stats={submitted=0,completed=0,coalesced=0};local o={format=format,hidden=true};local function send()mp.commandv('script-message','cinema-overlay',utils.format_json({id=id,data=o.data or '',hidden=o.hidden,z=o.z or 0,res_x=o.res_x or 1280,res_y=o.res_y or 720}))end;function o:update()self.hidden=false;stats.submitted=stats.submitted+1;stats.completed=stats.completed+1;send()end;function o:park()if not self.hidden then self.hidden=true;send()end end;function o:remove()self:park()end;function o:stats()return stats end;function o:prime(role)mp.set_property_bool('user-data/cinema-ready-'..role,true)end;return o end;return M`)
}
const preload=path.join(out,'preload.cjs')
await fs.writeFile(preload,`const {contextBridge,ipcRenderer}=require('electron');contextBridge.exposeInMainWorld('overlayHost',{onClock:callback=>ipcRenderer.on('overlay-clock',(_,sample)=>callback(sample)),onUI:callback=>ipcRenderer.on('overlay-ui',(_,sample)=>callback(sample)),onGeometry:callback=>ipcRenderer.on('overlay-geometry',(_,sample)=>callback(sample))})`)
const env={...process.env,DANMU_OVERLAY_TEST_CONFIG:JSON.stringify({root:process.cwd(),out,scripts,url,preload,videoTest})};delete env.ELECTRON_RUN_AS_NODE
let app;const report={passed:false,errors:[]},sleep=ms=>new Promise(r=>setTimeout(r,ms))
const summary=a=>{a=[...a].sort((a,b)=>a-b);return {n:a.length,p50:a[Math.floor(a.length*.5)],p95:a[Math.floor(a.length*.95)],p99:a[Math.floor(a.length*.99)],max:a.at(-1)}}
try{
 app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:[path.resolve('development/experiments/ass-overlay/main.cjs')],env,timeout:30000})
 const page=await app.firstWindow();page.on('console',m=>{if(m.type()==='error')report.errors.push(m.text())});page.on('pageerror',e=>report.errors.push(e.message))
 await page.waitForFunction(()=>!!window.assOverlay,{timeout:30000});await page.evaluate(()=>window.assOverlay.ready)
 const scene=layout(parseXml(await fs.readFile('artifacts/real-movie-sample.xml','utf8')),{fontSize:28,duration:19,region:.3,density:80})
 report.scene={items:scene.items.length};report.display=await app.evaluate(({screen})=>screen.getPrimaryDisplay());report.renderer=await page.evaluate(()=>({isolated:crossOriginIsolated,dpr:devicePixelRatio,width:innerWidth,height:innerHeight}))
 await page.evaluate(async scene=>{await window.assOverlay.load(scene);window.assOverlay.clock({time:20,speed:1,paused:false,reset:true})},scene)
 await sleep(150);report.visible=await page.evaluate(()=>window.assOverlay.diagnose());console.log(JSON.stringify({visibility:report.visible}));if(report.visible.alphaPixels<1000)throw Error('Danmu rendering produced no visible glyphs')
 if(videoTest){await page.evaluate(()=>window.assOverlay.primeUI());await app.evaluate(()=>global.testVideo());await sleep(1800);report.host=await app.evaluate(()=>global.player.ipc.command(['get_property','user-data/cinema-host']));if(!report.host?.attached)throw Error('Owned overlay did not attach');await app.evaluate(()=>global.overlay.showInactive());report.viewport=await page.evaluate(()=>({width:innerWidth,height:innerHeight,dpr:devicePixelRatio}));report.visibleAfterVideo=await page.evaluate(()=>window.assOverlay.diagnose());if(report.visibleAfterVideo.alphaPixels<1000)throw Error('Danmu disappeared after player geometry update');report.video=[]}
 await sleep(1500)
 const cdp=await page.context().newCDPSession(page)
 await cdp.send('Tracing.start',{categories:'viz,cc,gpu,benchmark',transferMode:'ReturnAsStream'})
 await page.evaluate(()=>window.assOverlay.stats(true))
 if(videoTest){
  for(const speed of [2,6]){
   await app.evaluate(async (_,speed)=>{await global.player.configure({holdSpeed:speed});await global.player.ipc.command(['seek',20,'absolute+exact'])},speed);await sleep(1200)
   const sample=()=>app.evaluate(async()=>{const names=['frame-drop-count','decoder-frame-drop-count','avsync','speed'];return Object.fromEntries(await Promise.all(names.map(async n=>[n,await global.player.ipc.command(['get_property',n])])))}),before=await sample()
   await app.evaluate(()=>global.player.ipc.command(['keydown','RIGHT']));await sleep(2500);const held=await sample();await app.evaluate(()=>global.player.ipc.command(['keyup','RIGHT']));await sleep(1400);const after=await sample()
   report.video.push({speed,entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],heldSpeed:held.speed,returnedSpeed:after.speed,avsync:after.avsync})
  }
 const uiBefore=await app.evaluate(()=>global.player.ipc.command(['get_property','frame-drop-count']));await page.evaluate(()=>window.assOverlay.uiStats(true));await app.evaluate(()=>global.player.ipc.command(['script-binding','cinema/cinema-settings']));await sleep(1500);await page.screenshot({path:path.join(out,'settings.png'),omitBackground:true,timeout:4000});await app.evaluate(()=>global.player.ipc.command(['keypress','ESC']));await sleep(500);report.ui=await page.evaluate(()=>window.assOverlay.uiStats());report.ui.drops=await app.evaluate(()=>global.player.ipc.command(['get_property','frame-drop-count']))-uiBefore;report.ui.cost=summary(report.ui.costs);delete report.ui.costs;
 }else await sleep(Number(process.env.DANMU_TEST_SECONDS||6)*1000)
 const stats=await page.evaluate(()=>window.assOverlay.stats());report.clock={time:stats.clock,target:stats.target,drift:stats.drift,coalesced:stats.coalesced};report.draws=stats.draws;report.intervals=summary(stats.times.slice(1).map((t,i)=>t-stats.times[i]));report.scheduler=summary(stats.rafTimes.slice(1).map((t,i)=>t-stats.rafTimes[i]));report.cost=summary(stats.costs);report.errors.push(...stats.errors)
 const done=new Promise(r=>cdp.once('Tracing.tracingComplete',r));await cdp.send('Tracing.end');const {stream}=await done;let trace=''
 for(;;){const chunk=await cdp.send('IO.read',{handle:stream});trace+=chunk.base64Encoded?Buffer.from(chunk.data,'base64').toString():chunk.data;if(chunk.eof)break}await cdp.send('IO.close',{handle:stream});await fs.writeFile(path.join(out,'trace.json'),trace)
 await page.screenshot({path:path.join(out,'overlay.png'),omitBackground:true,timeout:4000})
 const events=JSON.parse(trace).traceEvents,counts={};for(const e of events)if(/SwapBuffers|DrawFrame|Display::DrawAndSwap|FramePresented|PresentationFeedback/.test(e.name))counts[e.name]=(counts[e.name]||0)+1;report.compositorEvents=counts
 if(videoTest&&process.env.DANMU_TEST_MATRIX==='1'){
  const run=promisify(execFile),helper=path.join(out,'MpvWindowProbe.exe'),pid=await app.evaluate(()=>global.player.child.pid)
  await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+helper,path.resolve('development/tests/MpvWindowProbe.cs')],{windowsHide:true})
  report.matrix=[]
  for(const mode of ['window','maximized','fullscreen']){
   const before=await app.evaluate(()=>global.player.ipc.command(['get_property','frame-drop-count']))
   await app.evaluate((_,fullscreen)=>global.player.ipc.command(['set_property','fullscreen',fullscreen]),mode==='fullscreen')
   if(mode==='window'){await run(helper,['restore',String(pid)],{windowsHide:true});await run(helper,['resize',String(pid),'1160','720'],{windowsHide:true})}
   if(mode==='maximized')await run(helper,['maximize',String(pid)],{windowsHide:true})
   await sleep(500)
   for(const fps of [0,120,60]){
    await app.evaluate((_,fps)=>global.player.configure({danmuFps:fps}),fps)
    await page.evaluate(fps=>{window.assOverlay.configure({danmuFps:fps,enabled:true,opacity:.85,offset:0});window.assOverlay.stats(true)},fps)
    const began=performance.now();await sleep(1800);const duration=performance.now()-began,s=await page.evaluate(()=>window.assOverlay.stats())
    const sample={mode,fps,actualFps:s.draws*1000/duration,intervals:summary(s.times.slice(1).map((t,i)=>t-s.times[i])),drift:s.drift,coalesced:s.coalesced,viewport:await page.evaluate(()=>[innerWidth,innerHeight]),videoDrops:await app.evaluate(()=>global.player.ipc.command(['get_property','frame-drop-count']))-before}
    report.matrix.push(sample);console.log(JSON.stringify(sample))
   }
  }
 }
 report.zeroMissedFrames=report.intervals.max<1500/report.display.displayFrequency;report.passed=report.zeroMissedFrames&&report.errors.length===0&&report.intervals.p99<1500/report.display.displayFrequency&&(!videoTest||report.video.every(c=>c.entryDrops===0&&c.exitDrops===0&&c.decoderDrops===0&&c.heldSpeed===c.speed&&c.returnedSpeed===1&&Math.abs(c.avsync)<.05))
 if(videoTest)report.passed&&=Math.abs(report.clock.drift)<.05&&report.ui.drops===0&&report.ui.cost.max<1500/report.display.displayFrequency
 if(report.matrix)report.passed&&=report.matrix.every(c=>c.videoDrops===0&&Math.abs(c.drift)<.05&&Math.abs(c.actualFps-(c.fps||report.display.displayFrequency))<(c.fps||report.display.displayFrequency)*.05)
}catch(e){report.error=e.stack;process.exitCode=1}finally{if(videoTest&&app)await app.evaluate(()=>global.player?.stop()).catch(()=>{});await app?.close();await new Promise(r=>server.close(r));await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}
