import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/tray-cache-'+Date.now()),report={passed:false,checks:[],restores:[],memory:[],errors:[]}
await fs.mkdir(out,{recursive:true})
const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,'module.exports=api=>global.__cacheTest=api')
const env={...process.env,DANMU_TRAY_CACHE_MS:'2000',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_SCRIPT:hook,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const packaged=process.env.DANMU_PACKAGED_ROOT
const app=await _electron.launch({executablePath:packaged?path.join(packaged,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(packaged?[]:['.']),'--qa'],env,timeout:40000})
const check=(value,text)=>{assert(value,text);report.checks.push(text)},sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms))
const metrics=()=>app.evaluate(({app})=>app.getAppMetrics().map(p=>({pid:p.pid,type:p.type,memory:p.memory})))
try{
 let page=await app.firstWindow();page.setDefaultTimeout(15000);page.on('pageerror',error=>report.errors.push(error.message))
 const ready=async()=>{await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__cacheTest.getWindow();w.show();w.focus()})}
 await ready();await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor();await page.waitForTimeout(250)
 await page.evaluate(()=>{window.__cachedGallery=document.querySelector('.media-gallery');window.__cachedDocument=document.documentElement})
 const getPids=()=>app.evaluate(()=>global.__cacheTest.getWindow().webContents.mainFrame.framesInSubtree.map(f=>f.osProcessId))
 const originalPids=await getPids(),windowId=await app.evaluate(()=>global.__cacheTest.getWindow().id)
 report.memory.push({stage:'visible',processes:await metrics()})
 for(let n=0;n<3;n++){
  await app.evaluate(()=>global.__cacheTest.getWindow().close())
  check(await app.evaluate(()=>{const w=global.__cacheTest.getWindow();return !!w&&!w.isDestroyed()&&!w.isVisible()&&!w.webContents.getBackgroundThrottling()}),'quick tray close keeps one unthrottled cached window')
  await sleep(140)
  const start=Date.now(),firstFrame=await app.evaluate(async()=>{global.__cacheTest.getTray().emit('click');return global.__cacheTest.getWindow().webContents.executeJavaScript('new Promise(resolve=>{const start=performance.now();requestAnimationFrame(()=>resolve(performance.now()-start))})')}),elapsed=Date.now()-start
  report.restores.push({cycle:n,firstRafMs:firstFrame,elapsedMs:elapsed,pids:await getPids()})
  const same=await page.evaluate(()=>window.__cachedGallery===document.querySelector('.media-gallery')&&window.__cachedDocument===document.documentElement)
  check(same&&await app.evaluate(()=>global.__cacheTest.getWindow().id)===windowId,'tray restoration reuses the exact document and gallery DOM')
  check(firstFrame>=0&&elapsed<100,'restoration completes its first renderer callback within 100ms without a reload')
 }
 await sleep(2100)
 check(await app.evaluate(()=>!global.__cacheTest.getWindow().isDestroyed()&&global.__cacheTest.getWindow().isVisible()),'cancelled expiry cannot close a restored visible window')
 check(JSON.stringify(await getPids())===JSON.stringify(originalPids),'quick restore cycles do not allocate replacement renderer processes')
 await app.evaluate(()=>global.__cacheTest.getWindow().close())
 for(let n=0;n<100;n++){if(await app.evaluate(()=>!global.__cacheTest.getWindow()))break;await sleep(50)}
 check(await app.evaluate(()=>!global.__cacheTest.getWindow()),'long tray stay releases the native window after the bounded retention interval')
 let remaining=[];for(let n=0;n<80;n++){remaining=await metrics();if(!remaining.some(p=>originalPids.includes(p.pid)))break;await sleep(50)}
 check(!remaining.some(p=>originalPids.includes(p.pid)),'expiry releases all main-window renderer processes')
 report.memory.push({stage:'expired',processes:remaining})
 const next=app.waitForEvent('window');await app.evaluate(()=>global.__cacheTest.getTray().emit('click'));page=await next;await ready()
 check((await page.getByRole('heading',{name:'影片与弹幕',exact:true}).count())===1,'expired-cache restoration retains the selected page')
 await page.getByRole('tab',{name:'定时任务',exact:true}).click();await page.getByRole('spinbutton',{name:'倒计时小时',exact:true}).waitFor();check(true,'the recreated interface remains interactive')
 await app.evaluate(()=>{const w=global.__cacheTest.getWindow();w.close();w.webContents.forcefullyCrashRenderer()})
 for(let n=0;n<60;n++){if(await app.evaluate(()=>!global.__cacheTest.getWindow()))break;await sleep(50)}
 check(await app.evaluate(()=>!global.__cacheTest.getWindow()),'a crashed hidden renderer is discarded instead of restoring a dead window')
 const replacement=app.waitForEvent('window');await app.evaluate(()=>global.__cacheTest.getTray().emit('click'));page=await replacement;await ready();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor()
 check(true,'the next tray click recreates a usable page after a cached-renderer crash')
 check(!report.errors.length,'no renderer errors during normal tray cycles');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1;console.error(error)}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({out,...report},null,2))
