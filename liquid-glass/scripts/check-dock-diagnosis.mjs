import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import {runtime} from './test-runtime.mjs'
import fs from 'node:fs/promises'
import path from 'node:path'
import {execFile} from 'node:child_process'
import {promisify} from 'node:util'
import presentationTrace from './trace-presentation.cjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/dock-diagnosis-'+Date.now()),profile=path.join(out,'profile')
await fs.mkdir(profile,{recursive:true})
await fs.writeFile(path.join(profile,'window.json'),JSON.stringify({width:1320,height:880,maximized:true}))
const execute=promisify(execFile),powerHelper=path.join(out,'Power.exe')
if(process.env.DANMU_UI_POWER==='1')await execute(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+powerHelper,path.resolve('development/tests/ProcessPowerProbe.cs')],{windowsHide:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',...(process.env.DANMU_ELECTRON_ARGS||'').split('|').filter(Boolean)],env,timeout:40000}),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={variant:process.env.DANMU_DOCK_DIAGNOSTIC_VARIANT||'baseline',args:process.env.DANMU_ELECTRON_ARGS||'',phases:[]}
try{
 const page=await app.firstWindow()
 const priority=async()=>{if(process.env.DANMU_UI_PRIORITY==='1')report.processPriorities=await app.evaluate(({app})=>{const os=process.getBuiltinModule('node:os');return app.getAppMetrics().filter(m=>['Browser','GPU','Tab'].includes(m.type)).map(m=>{try{os.setPriority(m.pid,os.constants.priority.PRIORITY_ABOVE_NORMAL);return{type:m.type,priority:os.getPriority(m.pid)}}catch(error){return{type:m.type,error:error.message}}})})}
 await priority()
 const power=async()=>{if(process.env.DANMU_UI_POWER==='1'){const {root,pids}=await app.evaluate(({app})=>({root:process.pid,pids:app.getAppMetrics().filter(m=>['Browser','GPU','Tab'].includes(m.type)).map(m=>m.pid)}));const result=await execute(powerHelper,['apply',String(root),...pids.map(String)],{windowsHide:true});report.processPower=JSON.parse(result.stdout)}}
 await power()
 const earlyStyles={
  'promote-native-recording':'.dock-native-source .dock-recording{will-change:transform}',
  'promote-rail-material':'.dock-material{will-change:transform}',
  'promote-rail-and-native':'.dock-material,.dock-native-source .dock-recording{will-change:transform}',
  'promote-native-parts':'.dock-native-source .dock-recording,.dock-native-source .dock-source-rail,.dock-native-source .dock-source-selection{will-change:transform}',
  'promote-dock-body':'.dock-body{will-change:transform}.dock-recording{will-change:transform}',
  'simple-native-material':'.dock-native-source .dock-recorded-ambient,.dock-native-source .dock-source-rail{display:none!important}',
  'simple-native-and-shadow':'.dock-native-source .dock-recorded-ambient,.dock-native-source .dock-source-rail{display:none!important}.dock-lens-glass{box-shadow:none!important}'
 }
 if(earlyStyles[report.variant])await page.addStyleTag({content:earlyStyles[report.variant]})
 if(report.variant==='rgb-arithmetic')await page.evaluate(()=>{
  const patch=()=>{for(const old of document.querySelectorAll('filter[id^="dock-optics-"] feBlend')){const node=document.createElementNS('http://www.w3.org/2000/svg','feComposite');for(const name of ['in','in2','result'])if(old.hasAttribute(name))node.setAttribute(name,old.getAttribute(name));for(const [name,value]of Object.entries({operator:'arithmetic',k1:'0',k2:'1',k3:'1',k4:'0'}))node.setAttribute(name,value);old.replaceWith(node)}}
  patch();new MutationObserver(patch).observe(document.documentElement,{childList:true,subtree:true})
 })
 if(report.variant==='native-opaque')await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setOpacity(1))
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 const visualPage=page.frames().find(frame=>frame.url().startsWith('cinema-dock:'))||page
 report.isolated=visualPage!==page
 report.renderers=await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.mainFrame.framesInSubtree.map(frame=>({url:frame.url,pid:frame.osProcessId})))
 await priority()
 await power()
 report.rimRenderer=await visualPage.locator('.dock-hit-surface').getAttribute('data-rim-renderer')
 report.shaderReady=await visualPage.locator('.dock-hit-surface').getAttribute('data-shader-ready')
 if(process.env.DANMU_EXPERIMENTAL_DOCK_SHADER==='1'&&report.shaderReady!=='true')throw Error('Unified Dock shader failed to initialize')
 report.materialCache=await visualPage.locator('.dock-hit-surface').getAttribute('data-material-cache')
 if(process.env.DANMU_DOCK_CHECK_HOOKS==='1'){const pids=await app.evaluate(({app})=>app.getAppMetrics().map(p=>p.pid));const query='Get-Process -Id '+pids.join(',')+' -ErrorAction SilentlyContinue | ForEach-Object { $taskTarget=$_; try { $taskTarget.Modules | Where-Object { $_.ModuleName -match \'RTSS|Overlay|PresentMon|NvCamera\' } | Select-Object @{Name=\'ProcessId\';Expression={$taskTarget.Id}},ModuleName } catch {} } | ConvertTo-Json -Compress';try{report.externalHooks=(await execute('powershell.exe',['-NoProfile','-Command',query],{windowsHide:true})).stdout.trim()}catch(error){report.externalHookError=error.message}}
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 if(report.variant==='capture-static-material')await page.evaluate(async()=>{
  const root=document.querySelector('.dock-hit-surface'),r=root.getBoundingClientRect(),ratio=root.clientWidth/r.width
  const hide=document.createElement('style');hide.textContent='.dock-labels,.dock-resting-selection,.dock-lens{visibility:hidden!important}';document.head.append(hide)
  const painted=()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))
  await painted();const capture=await window.cinema.system('captureDock',{x:r.left-20/ratio,y:r.top-20/ratio,width:r.width+40/ratio,height:r.height+40/ratio});hide.remove()
  const source=new Image();source.src=capture.url;await source.decode()
  const css=document.createElement('style');css.textContent='.dock-material>*{visibility:hidden!important}.dock-native-source .dock-recorded-ambient,.dock-native-source .dock-source-rail{visibility:hidden!important}.dock-captured-static{position:absolute;max-width:none;pointer-events:none;visibility:visible!important}';document.head.append(css)
  for(const target of [root.querySelector('.dock-material'),root.querySelector('.dock-native-source .dock-recording')]){const image=source.cloneNode();image.className='dock-captured-static';Object.assign(image.style,{left:`${(capture.x-r.left)*ratio}px`,top:`${(capture.y-r.top)*ratio}px`,width:`${capture.width*ratio}px`,height:`${capture.height*ratio}px`});target.prepend(image)}
  await painted();await window.cinema.window('prime')
 })
 const styles={'dock-paint-boundary':'.bottom-navigation{contain:layout paint style;isolation:isolate;margin-top:-64px;padding-top:80px}@media(max-height:680px){.bottom-navigation{padding-top:77px}}', 'page-filter-cache':'.page-scene{filter:opacity(1)}', 'dock-layout-boundary':'.dock-hit-surface{contain:layout style}', 'page-layer-boundary':'.page-scene{contain:layout style;transform:translateZ(0)}.dock-hit-surface{contain:layout style}', 'no-page-fade':'.page-scene{opacity:1!important}', 'no-poster-backdrop':'.poster-count{backdrop-filter:none!important}', 'no-edge-mask':'.dock-prism-edge{mask-image:none!important}', 'no-rgb-filter':'.dock-prism-edge .dock-source-sample{filter:none!important}', 'no-shadow':'.dock-lens-glass{box-shadow:none!important}', 'no-label-clip':'.dock-labels{clip-path:none!important}', 'no-page-paint':'.page-scene{visibility:hidden!important}', 'no-lens':'.dock-lens{visibility:hidden!important}', 'no-native-source':'.dock-native-source{display:none!important}', 'no-raised-rim':'.dock-raised-rim{display:none!important}', 'no-veil':'.dock-lens-veil{display:none!important}'}
 if(styles[report.variant])await page.addStyleTag({content:styles[report.variant]})
 if(report.variant==='freeze-map-generation')await page.evaluate(()=>{const post=Worker.prototype.postMessage;Worker.prototype.postMessage=function(message,...args){if(message?.owner?.startsWith('dock-optics-'))return;return post.call(this,message,...args)}})
 if(report.variant==='freeze-map-images')await page.evaluate(()=>{const set=Element.prototype.setAttribute;Element.prototype.setAttribute=function(name,value){if(name==='href'&&this.tagName==='feImage'&&this.closest('filter')?.id.startsWith('dock-optics-'))return;return set.call(this,name,value)}})
 const rate=await app.evaluate(({screen})=>screen.getPrimaryDisplay().displayFrequency);report.hz=rate
 report.gpu=await app.evaluate(async({app})=>({features:app.getGPUFeatureStatus(),info:await app.getGPUInfo('complete'),versions:process.versions}))
 const cdp=await page.context().newCDPSession(page)
 if(process.env.DANMU_DOCK_COLLECT_GARBAGE==='1'){await cdp.send('HeapProfiler.collectGarbage');report.collectedStartupGarbage=true}
 let pointer={x:0,y:0,pressed:false};const rawInput=process.env.DANMU_DOCK_RAW_INPUT==='1';report.rawInput=rawInput
 const input=rawInput?{async move(x,y){pointer.x=x;pointer.y=y;await cdp.send('Input.dispatchMouseEvent',{type:'mouseMoved',x,y,button:'none',buttons:pointer.pressed?1:0})},async down(){pointer.pressed=true;await cdp.send('Input.dispatchMouseEvent',{type:'mousePressed',x:pointer.x,y:pointer.y,button:'left',buttons:1,clickCount:1})},async up(){pointer.pressed=false;await cdp.send('Input.dispatchMouseEvent',{type:'mouseReleased',x:pointer.x,y:pointer.y,button:'left',buttons:0,clickCount:1})}}:page.mouse
 const cpuProfiling=process.env.DANMU_DOCK_NO_CPU_PROFILE!=='1'
 const tracing=process.env.DANMU_DOCK_NO_TRACE!=='1';report.tracing=tracing
 if(cpuProfiling){await cdp.send('Profiler.enable');await cdp.send('Profiler.setSamplingInterval',{interval:500})}
 await page.evaluate(()=>{const scene=document.getElementById('workspace-page');new MutationObserver(()=>console.timeStamp('page:'+scene.dataset.page)).observe(scene,{attributes:true,attributeFilter:['data-page']})})
 for(const name of (process.env.DANMU_DOCK_PHASES||'same-slot-cold,across-tabs,same-slot-warm').split(',')){
  const selected=page.locator('[role=tab][aria-selected=true]'),a=await selected.boundingBox(),b=await page.getByRole('tab',{name:'定时任务',exact:true}).boundingBox()
  if(tracing)await cdp.send('Tracing.start',{categories:process.env.DANMU_DOCK_TRACE_CATEGORIES||'blink.user_timing,cc,viz,benchmark',transferMode:'ReturnAsStream'});if(cpuProfiling)await cdp.send('Profiler.start')
  await visualPage.evaluate(()=>{window.__diagnosticFrames=[];window.__diagnosticPoses=[];window.__diagnosticActive=true;window.__diagnosticLift=0;window.__diagnosticHeld=false;const tick=t=>{if(window.__diagnosticActive){window.__diagnosticFrames.push(t);const dock=document.querySelector('.dock-hit-surface');window.__diagnosticPoses.push({t,motion:dock?.dataset.motion,held:dock?.dataset.held,page:dock?.dataset.selected});window.__diagnosticLift=Math.max(window.__diagnosticLift,Number(dock?.dataset.lift||0));window.__diagnosticHeld||=dock?.dataset.held==='true';requestAnimationFrame(tick)}};requestAnimationFrame(tick)})
  if(report.isolated)await page.evaluate(()=>{window.__parentFrames=[];window.__parentActive=true;const tick=t=>{if(window.__parentActive){window.__parentFrames.push(t);requestAnimationFrame(tick)}};requestAnimationFrame(tick)})
  await visualPage.evaluate(name=>{performance.mark('dock-start:'+name);console.timeStamp('dock-start:'+name)},name)
  await input.move(a.x+a.width/2,a.y+a.height/2);await input.down()
  for(let i=0;i<80;i++){const travel=name==='across-tabs'?b.x+b.width/2-a.x-a.width/2:7*Math.sin(i/79*Math.PI*2);await input.move(a.x+a.width/2+travel*(name==='across-tabs'?i/79:1),a.y+a.height/2);await sleep(8)}
  await input.up();await sleep(400)
  await visualPage.evaluate(name=>{performance.mark('dock-end:'+name);console.timeStamp('dock-end:'+name)},name)
  const dt=await visualPage.evaluate(()=>{window.__diagnosticActive=false;return window.__diagnosticFrames.slice(1).map((t,i)=>t-window.__diagnosticFrames[i])})
  const parentDt=report.isolated?await page.evaluate(()=>{window.__parentActive=false;return window.__parentFrames.slice(1).map((t,i)=>t-window.__parentFrames[i])}):dt
  const lateFrames=await visualPage.evaluate(budget=>window.__diagnosticPoses.slice(1).map((pose,i)=>({ms:pose.t-window.__diagnosticPoses[i].t,before:window.__diagnosticPoses[i],after:pose})).filter(frame=>frame.ms>budget),1500/rate)
  const motionMissed=lateFrames.filter(frame=>frame.before.held==='true'||frame.after.held==='true'||frame.before.motion==='running'||frame.after.motion==='running').length
  if(cpuProfiling){const cpu=await cdp.send('Profiler.stop');await fs.writeFile(path.join(out,name+'.cpuprofile'),JSON.stringify(cpu.profile))}
  let events=[]
  if(tracing)await sleep(100)
  if(tracing){const done=new Promise(r=>cdp.once('Tracing.tracingComplete',r));await cdp.send('Tracing.end');const {stream}=await done;let trace=''
   for(;;){const part=await cdp.send('IO.read',{handle:stream});trace+=part.base64Encoded?Buffer.from(part.data,'base64').toString():part.data;if(part.eof)break}await cdp.send('IO.close',{handle:stream});await fs.writeFile(path.join(out,name+'.json'),trace);events=JSON.parse(trace).traceEvents}
  const begin=presentationTrace.marker(events,'dock-start:'+name),end=presentationTrace.marker(events,'dock-end:'+name)
  const presentation=begin&&end?presentationTrace.presentationSummary(presentationTrace.presentedFrames(events),{pid:begin.pid,start:begin.ts,end:end.ts,names:['MainThreadAnimation','CompositorAnimation','NativePropertyAnimation','RAF','JSAnimation','CanvasAnimation']}):{available:false,tracks:[],zeroDropped:false}
  const layouts=events.filter(e=>e.name==='Layout'&&e.ph==='X').map(e=>e.dur/1000),sorted=[...dt].sort((a,b)=>a-b)
  const gpu=events.filter(e=>['RasterDecoderImpl::DoEndRasterCHROMIUM::Flush','RasterDecoderImpl::DoFlushTileRasterGraphiteCommandsCHROMIUM'].includes(e.name)&&e.ph==='X').map(e=>e.dur/1000)
  const gesture={...await visualPage.evaluate(()=>({held:window.__diagnosticHeld,maxLift:window.__diagnosticLift})),...await page.evaluate(()=>({page:document.querySelector('#workspace-page')?.dataset.page,selected:document.querySelector('[role=tab][aria-selected=true]')?.id}))}
  const result={variant:report.variant,name,presentation,frames:dt.length,p95:sorted[Math.floor(sorted.length*.95)],max:Math.max(...dt),missed:dt.filter(t=>t>1500/rate).length,motionMissed,lateFrames,parentMax:Math.max(...parentDt),parentMissed:parentDt.filter(t=>t>1500/rate).length,shaderReady:await visualPage.locator('.dock-hit-surface').getAttribute('data-shader-ready'),shaderDiagnostics:await visualPage.locator('.dock-hit-surface').getAttribute('data-shader-diagnostics'),layoutTotal:layouts.reduce((a,b)=>a+b,0),layoutMax:Math.max(...layouts),gpuPeak:Math.max(...gpu),gpuOverBudget:gpu.filter(t=>t>1000/rate).length,gesture,valid:gesture.held&&gesture.maxLift>.8&&(name!=='across-tabs'||gesture.page==='schedule')};report.phases.push(result);console.log(JSON.stringify(result))
 }
 report.passed=report.phases.length>0&&report.phases.every(p=>p.valid&&p.presentation.available&&p.presentation.zeroDropped)
 if(!report.passed)process.exitCode=1
}finally{await app.close();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(out)}
