import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
import {execFile} from 'node:child_process'
import {promisify} from 'node:util'
import presentationTrace from './trace-presentation.cjs'
const run=promisify(execFile)
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/modal-performance-'+Date.now())
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:path.join(out,'bridge.cjs')};delete env.ELECTRON_RUN_AS_NODE
await fs.writeFile(env.DANMU_QA_SCRIPT,'module.exports=api=>global.__modalTest=api')
const exe=process.env.DANMU_TEST_DESKTOP||path.resolve('tools/electron/electron.exe')
const app=await _electron.launch({executablePath:exe,args:[...(process.env.DANMU_TEST_DESKTOP?[]:['.']),'--qa',...(process.env.DANMU_ELECTRON_ARGS||'').split('|').filter(Boolean)],env,timeout:40000}),report={parameters:{args:process.env.DANMU_ELECTRON_ARGS||'',css:process.env.DANMU_PERF_CSS||'',traceCategories:process.env.DANMU_MODAL_TRACE_CATEGORIES||'blink.user_timing,cc,viz,benchmark'},phases:[],errors:[],checks:[]}
const quantile=(a,p)=>a.slice().sort((a,b)=>a-b)[Math.min(a.length-1,Math.floor(a.length*p))]
async function hooks(){
 const roots=await app.evaluate(({app})=>[...app.getAppMetrics().map(p=>p.pid),global.__modalTest.getPlayer().child?.pid,global.__modalTest.getPlayer().overlay?.child?.pid].filter(Number.isInteger))
 const query=`$ids=[Collections.Generic.HashSet[int]]::new();${roots.map(pid=>`$null=$ids.Add(${pid});`).join('')}$all=Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId;for($n=0;$n -lt 5;$n++){foreach($p in $all){if($ids.Contains([int]$p.ParentProcessId)){$null=$ids.Add([int]$p.ProcessId)}}};$errors=@();$hooks=@(foreach($id in $ids){try{(Get-Process -Id $id -ErrorAction Stop).Modules | Where-Object {$_.ModuleName -match 'RTSS|PresentMon|NvCamera'} | Select-Object @{Name='pid';Expression={$id}},ModuleName}catch{$errors+=$id}});[pscustomobject]@{rtssRunning=[bool](Get-Process RTSS -ErrorAction SilentlyContinue);hooks=$hooks;errors=$errors}|ConvertTo-Json -Compress`
 try{return JSON.parse((await run('powershell.exe',['-NoProfile','-Command',query],{windowsHide:true})).stdout)}catch(error){return{error:error.message}}
}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(15000);page.on('pageerror',e=>report.errors.push(e.message))
 if(process.env.DANMU_PERF_CSS)await page.addStyleTag({content:process.env.DANMU_PERF_CSS})
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await app.evaluate((_,side)=>{const w=global.__modalTest.getWindow();if(side){w.setContentSize(940,640);w.setPosition(0,0)}else w.maximize();w.show();w.focus()},process.env.DANMU_TEST_SIDE_BY_SIDE==='1')
 report.hz=await app.evaluate(({screen})=>screen.getPrimaryDisplay().displayFrequency)
 report.effectiveSwitches=await app.evaluate(({app})=>({disabledFeatures:app.commandLine.getSwitchValue('disable-features'),enabledFeatures:app.commandLine.getSwitchValue('enable-features'),disableDirectComposition:app.commandLine.hasSwitch('disable-direct-composition'),disableVideoOverlays:app.commandLine.hasSwitch('disable-direct-composition-video-overlays')}))
 report.hookCollectionTiming='after timed phases; no module scans during animations'
 const cdp=await page.context().newCDPSession(page)
 await cdp.send('Tracing.start',{categories:report.parameters.traceCategories,transferMode:'ReturnAsStream'})
 await page.evaluate(()=>{window.__modalFrames=[];const sample=t=>{window.__modalFrames.push(t);requestAnimationFrame(sample)};requestAnimationFrame(sample)})
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor()
 for(const playback of [false,true]){
  if(playback){await app.evaluate(async(_,video)=>{const p=global.__modalTest.getPlayer();p.extraArgs=['--geometry=960x540-0-0'];await p.configure({resume:false,volume:0,danmuFps:0});await p.play([{key:'test',path:video,title:'弹窗并行播放测试'}],'test');await p.ipc.command(['set_property','fullscreen',false])},path.resolve(process.env.DANMU_TEST_MEDIA||'artifacts/overlay-4k-fixture/test.mkv'));await page.waitForTimeout(1200);await app.evaluate(()=>global.__modalTest.getWindow().focus())}
  for(const name of ['本地播放设置','本地播放设置','管理接口','管理接口']){
   const button=page.getByRole('button',{name,exact:true});await page.mouse.move(3,3);await page.waitForTimeout(400)
   const before=playback?await app.evaluate(async()=>{const p=global.__modalTest.getPlayer();await p.overlay.probe(true);return await p.ipc.command(['get_property','frame-drop-count'])}):null
   await button.evaluate(n=>{window.__modalFrames=[];window.__modalBegan=performance.now();performance.mark('modal-start:'+window.__modalBegan);console.timeStamp('modal-start:'+window.__modalBegan);n.click()});await page.waitForTimeout(650);await page.getByRole('dialog').waitFor()
   const data=await page.evaluate(()=>{const times=window.__modalFrames,start=window.__modalBegan,dt=times.slice(1).map((t,i)=>t-times[i]),node=document.querySelector('.modal-layer:not([inert]) .modal-presentation[data-animation-start]');return{start,times,dt,firstFrame:times[0]-start,nativeStart:node?Number(node.dataset.animationStart):null,nativeDuration:node?Number(node.dataset.animationDuration):null}})
   if(data.nativeStart!==null&&data.nativeStart<data.start)throw Error('The new dialog animation has not started; stale metadata cannot certify frame pacing')
   const during=playback?await app.evaluate(async()=>{const p=global.__modalTest.getPlayer(),stats=await p.overlay.probe();return{videoDrops:await p.ipc.command(['get_property','frame-drop-count']),danmuMisses:stats.frameMisses,gpuSkipped:stats.gpu.skipped}}):null
   report.phases.push({playback,name,start:data.start,nativeStart:data.nativeStart,nativeDuration:data.nativeDuration,firstFrame:data.firstFrame,frames:data.dt.length,p50:quantile(data.dt,.5),p95:quantile(data.dt,.95),p99:quantile(data.dt,.99),max:Math.max(...data.dt),missed:data.dt.filter(t=>t>1500/report.hz).length,...(during?{videoDrops:during.videoDrops-before,danmuMisses:during.danmuMisses,gpuSkipped:during.gpuSkipped}:{})})
   if(!playback)await page.screenshot({path:path.join(out,`dialog-${report.phases.length}.png`)});await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'})
   console.log(JSON.stringify(report.phases.at(-1)))
  }
 }
 report.mainHooks=report.playbackHooks=await hooks()
 const done=new Promise(resolve=>cdp.once('Tracing.tracingComplete',resolve));await cdp.send('Tracing.end');const {stream}=await done;let trace='';for(;;){const part=await cdp.send('IO.read',{handle:stream});trace+=part.base64Encoded?Buffer.from(part.data,'base64').toString():part.data;if(part.eof)break}await cdp.send('IO.close',{handle:stream});await fs.writeFile(path.join(out,'trace.json'),trace)
 const events=JSON.parse(trace).traceEvents,presented=presentationTrace.presentedFrames(events)
 for(const phase of report.phases){const marker=presentationTrace.marker(events,'modal-start:'+phase.start);if(!marker)throw Error('Missing modal timing marker');const start=marker.ts+(phase.nativeStart-phase.start)*1000;phase.presentation=presentationTrace.presentationSummary(presented,{pid:marker.pid,start,end:start+phase.nativeDuration*1000})}
 for(const phase of report.phases){const start=presentationTrace.marker(events,'modal-start:'+phase.start)?.ts,animationStart=start+(phase.nativeStart===null?0:phase.nativeStart-phase.start)*1000,duration=phase.nativeDuration??650;const swaps=events.filter(e=>e.name==='SkiaOutputSurfaceImplOnGpu::SwapBuffers'&&e.ts>=animationStart&&e.ts<animationStart+duration*1000).map(e=>e.ts).sort((a,b)=>a-b),dt=swaps.slice(1).map((t,i)=>(t-swaps[i])/1000);phase.compositor={firstFrame:(swaps[0]-start)/1000,animationFirstFrame:(swaps[0]-animationStart)/1000,frames:dt.length,p95:quantile(dt,.95),max:Math.max(...dt),missed:dt.filter(t=>t>1500/report.hz).length}}
 await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();const parent=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await parent.waitFor();await page.waitForTimeout(500)
 const before=await parent.boundingBox();await parent.getByRole('button',{name:'管理接口',exact:true}).click();const child=page.getByRole('dialog',{name:'管理接口',exact:true});await child.waitFor();await page.waitForTimeout(400);await page.keyboard.press('Escape');await child.waitFor({state:'detached'});await page.waitForTimeout(300)
 if(!await parent.isVisible())throw Error('closing child removed parent');const after=await parent.boundingBox();if(['x','y','width','height'].some(k=>Math.abs(before[k]-after[k])>.2))throw Error('parent geometry changed on child close');report.checks.push('nested ESC restores parent without geometry jump')
 if(await page.locator('.workspace').evaluate(el=>getComputedStyle(el).filter)!=='blur(9px)')throw Error('background blur lost');report.checks.push('9px source blur retained after nested close')
 await page.keyboard.press('Escape');await parent.waitFor({state:'detached'});report.checks.push('parent closes independently')
 const playbackStats=()=>app.evaluate(async()=>{const p=global.__modalTest.getPlayer();return {active:p.state.active,drops:await p.ipc.command(['get_property','frame-drop-count']),decoderDrops:await p.ipc.command(['get_property','decoder-frame-drop-count'])}})
 const beforeRestore=await playbackStats(),nextWindow=app.waitForEvent('window',{timeout:20000})
 await app.evaluate(()=>{global.__modalTest.getWindow().close();global.__modalTest.show()});const restored=await nextWindow;restored.setDefaultTimeout(15000)
 await restored.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');if(await restored.locator('[data-startup-warmup]').count())throw Error('temporary startup form was retained after warmup');const retained=restored.locator('[data-retained-dialog]');const parked=await retained.evaluateAll(nodes=>nodes.map(n=>({name:n.dataset.retainedDialog,hidden:n.getAttribute('aria-hidden'),inert:n.inert})));if(new Set(parked.map(n=>n.name)).size!==parked.length||parked.some(n=>n.hidden!=='true'||!n.inert))throw Error('parked forms must be unique and inaccessible')
 await restored.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await restored.getByRole('button',{name:'本地播放设置',exact:true}).click();await restored.getByRole('dialog').waitFor();await restored.waitForTimeout(600)
 const afterRestore=await playbackStats();report.restoreDuringPlayback={active:afterRestore.active,videoDrops:afterRestore.drops-beforeRestore.drops,decoderDrops:afterRestore.decoderDrops-beforeRestore.decoderDrops}
 if(!afterRestore.active||report.restoreDuringPlayback.videoDrops!==0||report.restoreDuringPlayback.decoderDrops!==0)throw Error('foreground restoration disrupted playback')
 report.checks.push('foreground recreation and first dialog do not drop video frames; warmup DOM released')
 report.functionalPassed=report.errors.length===0
 report.mainThreadZeroMissedFrames=report.phases.every(p=>p.missed===0)
 report.zeroMissedSubmitIntervals=report.phases.every(p=>p.compositor.frames>1&&p.compositor.missed===0)
 report.zeroMissedFrames=report.phases.every(p=>p.presentation.available&&p.presentation.zeroDropped&&(!p.playback||p.videoDrops===0&&p.danmuMisses===0&&p.gpuSkipped===0))
 report.passed=report.functionalPassed&&report.zeroMissedFrames
 if(!report.passed)process.exitCode=1
}catch(error){report.error=error.stack;process.exitCode=1;console.error(error)}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(out)}
