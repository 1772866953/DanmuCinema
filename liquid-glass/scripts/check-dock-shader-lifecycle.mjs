import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/dock-shader-lifecycle-'+Date.now()),report={passed:false,cases:[],errors:[]}
await fs.mkdir(out,{recursive:true})
for(const density of (process.env.DANMU_DOCK_DENSITIES||'1,1.25,2').split(',').map(Number)){
 const profile=path.join(out,'profile-'+density),hook=path.join(out,'hook.cjs')
 await fs.mkdir(profile,{recursive:true});await fs.writeFile(hook,'module.exports=api=>global.__dockLifecycle=api')
 const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook,DANMU_EXPERIMENTAL_DOCK_SHADER:'1'};delete env.ELECTRON_RUN_AS_NODE
 const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor='+density],env,timeout:40000})
 const check=(condition,name)=>{assert(condition,name);report.cases.push({density,name})}
 try{
  let page=await app.firstWindow();page.setDefaultTimeout(20000);page.on('pageerror',e=>report.errors.push(e.message))
  const ready=async()=>{await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true'&&document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true');await app.evaluate(()=>{const w=global.__dockLifecycle.getWindow();w.show();w.focus()})}
  await ready()
  for(const [width,height,zoom]of [[1320,880,1],[940,640,1],[940,640,1.2],[1320,880,1.2]]){
   await app.evaluate((_,g)=>{const w=global.__dockLifecycle.getWindow();w.webContents.setZoomFactor(g.zoom);w.setContentSize(g.width,g.height)}, {width,height,zoom})
   await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true');await page.waitForTimeout(500)
   const rail=page.locator('.dock-hit-surface'),sample=await rail.evaluate(el=>({density:devicePixelRatio,width:el.clientWidth,height:el.clientHeight,canvas:el.querySelector('.dock-shader-canvas').width,labels:[...el.querySelectorAll('.dock-body>.dock-labels>.dock-label')].map(n=>({text:n.textContent,size:getComputedStyle(n).fontSize})),material:el.querySelector('.dock-native-material').naturalWidth,diagnostics:JSON.parse(el.dataset.shaderDiagnostics)}))
   check(sample.canvas>sample.width&&sample.material>sample.width&&sample.labels.length===8,`native labels and full-resolution material at ${width} / ${zoom}: ${JSON.stringify(sample)}`)
   const first=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),target=await page.getByRole('tab',{name:'影片与弹幕',exact:true}).boundingBox()
   await page.mouse.move(first.x+first.width/2,first.y+first.height/2);await page.mouse.down();await page.mouse.move(target.x+target.width*.75,target.y+target.height/2-3,{steps:12});await page.waitForTimeout(220)
   check(await rail.getAttribute('data-shader-ready')==='true'&&Number(await rail.getAttribute('data-lift'))>.7,'raised optics survive resize and zoom')
   await fs.writeFile(path.join(out,`held-${density}-${width}-${zoom}.png`),Buffer.from(await app.evaluate(async()=> (await global.__dockLifecycle.getWindow().webContents.capturePage()).toPNG().toString('base64')),'base64'));await page.mouse.up();await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled')
   check(await rail.evaluate(el=>getComputedStyle(el.querySelector('.dock-body>.dock-labels')).clipPath==='none'),'idle handoff restores unmasked native text')
  }
  await page.emulateMedia({forcedColors:'active'});await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='false')
  check(await page.locator('.dock-shader-canvas').evaluate(el=>getComputedStyle(el).display==='none'),'forced colors use native fallback')
  await page.emulateMedia({forcedColors:'none'});await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true')
  await page.locator('.dock-shader-canvas').evaluate(el=>{const ext=el.getContext('webgl2').getExtension('WEBGL_lose_context');if(!ext)throw Error('No context-loss test extension');window.__restoreDock=()=>ext.restoreContext();ext.loseContext()})
  await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='false');await page.waitForTimeout(200);await page.evaluate(()=>window.__restoreDock());await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true')
  const d=await page.locator('.dock-hit-surface').evaluate(el=>JSON.parse(el.dataset.shaderDiagnostics));check(d.contextLosses===1&&d.contextRestores===1,'context loss falls back and restores its textures')
  const pid=await app.evaluate(()=>global.__dockLifecycle.getWindow().webContents.getOSProcessId()),next=app.waitForEvent('window',{timeout:25000})
  await app.evaluate(()=>{global.__dockLifecycle.getWindow().close();global.__dockLifecycle.show()});page=await next;await ready()
  const metrics=await app.evaluate(({app})=>app.getAppMetrics())
  check(!metrics.some(p=>p.pid===pid),'tray destroys the previous renderer')
  check(await page.locator('[data-startup-warmup]').count()===0&&await page.locator('.page-content').count()===1,'restoration retains no temporary forms or hidden pages')
 }catch(error){const page=await app.firstWindow();report.errors.push({density,error:error.stack,pose:await page.locator('.dock-hit-surface').evaluate(el=>({...el.dataset,viewport:[innerWidth,innerHeight],bounds:el.getBoundingClientRect().toJSON()})).catch(()=>null)});await page.screenshot({path:path.join(out,'failure-'+density+'.png')}).catch(()=>{})}finally{await app.close()}
}
report.passed=report.errors.length===0;await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report}));if(!report.passed)process.exitCode=1
