import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/startup-readiness-'+Date.now()),profile=path.join(out,'profile'),hook=path.join(out,'hook.cjs'),report={passed:false,cycles:[]}
await fs.mkdir(profile,{recursive:true})
await fs.writeFile(path.join(profile,'window.json'),JSON.stringify({width:1320,height:880,maximized:true}))
await fs.writeFile(hook,`module.exports=api=>{global.__startupTest=api;global.__startupReady=[];const watch=w=>{let completed=0;const capture=w.webContents.capturePage.bind(w.webContents);w.webContents.capturePage=async(...args)=>{const result=await capture(...args);completed++;return result};w.once('content-ready',()=>global.__startupReady.push({maximized:w.isMaximized(),gpuFrameComplete:completed>0,bounds:w.getBounds()}))};watch(api.getWindow());api.app.on('browser-window-created',(_,w)=>watch(w))}`)
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa'],env,timeout:40000}),sleep=ms=>new Promise(r=>setTimeout(r,ms))
try{
 let page=await app.firstWindow()
 for(let cycle=0;cycle<3;cycle++){
  await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
  const warmups=await page.locator('[data-startup-warmup]').count(),ready=await app.evaluate(()=>global.__startupReady.at(-1))
  assert.equal(warmups,0);assert(ready.gpuFrameComplete);assert(ready.maximized)
  const result={cycle,warmups,...ready};report.cycles.push(result)
  if(cycle===2)break
  const oldPid=await app.evaluate(()=>global.__startupTest.getWindow().webContents.getOSProcessId())
  await app.evaluate(()=>global.__startupTest.getWindow().close())
  for(let n=0;n<120;n++){const released=await app.evaluate(({app},pid)=>!app.getAppMetrics().some(p=>p.pid===pid),oldPid);if(released){result.rendererReleased=true;break}await sleep(25)}
  assert(result.rendererReleased,'tray must destroy the renderer')
  const next=app.waitForEvent('window',{timeout:20000}),began=Date.now();await app.evaluate(()=>global.__startupTest.getTray().emit('click'));page=await next
  await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');result.restoreReadyMs=Date.now()-began
 }
 report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}finally{await app.close();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}
