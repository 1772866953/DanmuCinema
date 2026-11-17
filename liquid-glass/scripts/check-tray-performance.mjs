import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/tray-performance-'+Date.now()),profile=path.join(out,'profile')
await fs.mkdir(profile,{recursive:true})
await fs.writeFile(path.join(profile,'window.json'),JSON.stringify({width:1320,height:880,maximized:true}))
const hook=path.join(out,'hook.cjs')
await fs.writeFile(hook,`module.exports=api=>{global.__trayPerf=api;global.__readyStates=[];const watch=w=>{let ready=false,capturing=0,completed=0;const capture=w.webContents.capturePage.bind(w.webContents);w.webContents.capturePage=async(...args)=>{capturing++;try{return await capture(...args)}finally{capturing--;completed++}};w.once('content-ready',()=>{ready=true;global.__readyStates.push({bounds:w.getBounds(),maximized:w.isMaximized(),gpuFrameComplete:completed>0&&capturing===0,resizes:[]})});w.on('resize',()=>{if(ready)global.__readyStates.at(-1).resizes.push(w.getBounds())})};watch(api.getWindow());api.app.on('browser-window-created',(_,w)=>watch(w))}`)
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',...(process.env.DANMU_ELECTRON_ARGS||'').split('|').filter(Boolean)],env,timeout:40000}),report={passed:false,zeroMissedFrames:false,args:process.env.DANMU_ELECTRON_ARGS||'',cycles:[]}
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),quantile=(a,p)=>[...a].sort((x,y)=>x-y)[Math.min(a.length-1,Math.floor(a.length*p))]
try{
 let page=await app.firstWindow()
 const rate=await app.evaluate(({screen})=>screen.getPrimaryDisplay().displayFrequency)
 report.displayHz=rate;report.frameBudget=1000/rate
 for(let cycle=0;cycle<3;cycle++){
  await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
  assert.equal(await page.locator('[data-startup-warmup]').count(),0,'temporary warmups must finish before presentation readiness')
  await app.evaluate(()=>{const w=global.__trayPerf.getWindow();w.show();w.focus()})
  const ready=await app.evaluate(()=>global.__readyStates.at(-1)),bounds=await app.evaluate(()=>global.__trayPerf.getWindow().getBounds())
  assert(ready.maximized,'maximize must precede optics readiness')
  assert.deepEqual(ready.bounds,bounds,'revealing the window must not resize the primed scene')
  const cdp=await page.context().newCDPSession(page)
  await cdp.send('Tracing.start',{categories:'devtools.timeline,cc,viz,gpu,benchmark',transferMode:'ReturnAsStream'})
  await page.evaluate(()=>{window.__trayFrames=[];window.__traySampling=true;const frame=t=>{if(window.__traySampling){window.__trayFrames.push(t);requestAnimationFrame(frame)}};requestAnimationFrame(frame)})
  const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'定时任务',exact:true}).boundingBox()
  await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down()
  for(let i=0;i<64;i++){await page.mouse.move(a.x+a.width/2+(b.x+b.width/2-a.x-a.width/2)*i/63,a.y+a.height/2);await sleep(8)}
  await page.mouse.up();await sleep(300)
  const dt=await page.evaluate(()=>{window.__traySampling=false;return window.__trayFrames.slice(1).map((t,i)=>t-window.__trayFrames[i])})
  const done=new Promise(resolve=>cdp.once('Tracing.tracingComplete',resolve));await cdp.send('Tracing.end');const {stream}=await done;let trace=''
  for(;;){const chunk=await cdp.send('IO.read',{handle:stream});trace+=chunk.base64Encoded?Buffer.from(chunk.data,'base64').toString():chunk.data;if(chunk.eof)break}
  await cdp.send('IO.close',{handle:stream});await fs.writeFile(path.join(out,`trace-${cycle}.json`),trace)
  const oldPid=await app.evaluate(()=>global.__trayPerf.getWindow().webContents.getOSProcessId()),before=await app.evaluate(({app})=>app.getAppMetrics())
  const completedReady=await app.evaluate(()=>global.__readyStates.at(-1));assert(completedReady.gpuFrameComplete,'the final GPU frame must complete before presentation readiness')
  const timing={cycle,p50:quantile(dt,.5),p95:quantile(dt,.95),p99:quantile(dt,.99),max:Math.max(...dt),missed:dt.filter(t=>t>1000/rate*1.5).length,ready:completedReady,bounds}
  if(cycle===2){report.cycles.push(timing);break}
  await app.evaluate(()=>global.__trayPerf.getWindow().close())
  let metrics=[];for(let i=0;i<100;i++){metrics=await app.evaluate(({app})=>app.getAppMetrics());if(!metrics.some(p=>p.pid===oldPid))break;await sleep(30)}
  timing.rendererReleased=!metrics.some(p=>p.pid===oldPid);assert(timing.rendererReleased,'tray must release the renderer')
  timing.memory={before:before.map(p=>({type:p.type,privateBytes:p.memory.privateBytes})),after:metrics.map(p=>({type:p.type,privateBytes:p.memory.privateBytes}))};report.cycles.push(timing)
  const next=app.waitForEvent('window',{timeout:20000}),started=Date.now();await app.evaluate(()=>global.__trayPerf.getTray().emit('click'));page=await next;await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');timing.restoreReadyMs=Date.now()-started
 }
 report.passed=report.cycles.every(c=>c.ready.maximized&&c.ready.gpuFrameComplete&&c.ready.resizes.length===0&&c.rendererReleased!==false)
 report.zeroMissedFrames=report.cycles.every(c=>c.missed===0)
}catch(error){report.error=error.stack;process.exitCode=1}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report},null,2))}
