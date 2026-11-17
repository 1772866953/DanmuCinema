import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/isolated-dock-lifecycle-'+Date.now()),report={passed:false,checks:[],errors:[],memory:[]}
await fs.mkdir(out,{recursive:true})
for(const density of (process.env.DANMU_DOCK_DENSITIES||'1,1.25,2').split(',').map(Number)){
 const profile=path.join(out,'profile-'+density),hook=path.join(out,'hook.cjs');await fs.mkdir(profile,{recursive:true});await fs.writeFile(hook,'module.exports=api=>global.__isolatedTest=api')
 const env={...process.env,DANMU_ISOLATED_DOCK:'1',DANMU_EXPERIMENTAL_DOCK_SHADER:'1',DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_SCRIPT:hook,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
 const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--enable-features=SkiaGraphite','--force-device-scale-factor='+density],env,timeout:40000})
 const check=(condition,name)=>{assert(condition,name);report.checks.push({density,name})}
 try{
  let page=await app.firstWindow();page.setDefaultTimeout(15000);page.on('pageerror',e=>report.errors.push(e.message))
  const ready=async()=>{await page.waitForFunction(()=>document.querySelector('[data-isolated=true]')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__isolatedTest.getWindow();w.show();w.focus()});return page.frames().find(f=>f.url().startsWith('cinema-dock:'))}
  let dock=await ready();check(Boolean(dock),'independent frame became ready')
  for(const [width,height,zoom]of [[1320,880,1],[940,640,1],[940,640,1.2],[1320,880,1.2]]){
   await app.evaluate((_,g)=>{const w=global.__isolatedTest.getWindow();w.webContents.setZoomFactor(g.zoom);w.setContentSize(g.width,g.height)},{width,height,zoom});await page.waitForTimeout(600)
   await dock.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true')
   const parent=await page.locator('[data-isolated=true]').boundingBox(),iframe=await page.locator('iframe').boundingBox(),child=await dock.locator('.dock-hit-surface').evaluate(el=>({r:el.getBoundingClientRect().toJSON(),width:innerWidth,height:innerHeight,labels:el.querySelectorAll('.dock-body>.dock-labels>.dock-label').length,font:getComputedStyle(el.querySelector('.dock-label')).fontSize}))
   const actual={x:iframe.x+child.r.x*iframe.width/child.width,y:iframe.y+child.r.y*iframe.height/child.height,width:child.r.width*iframe.width/child.width,height:child.r.height*iframe.height/child.height}
   check(['x','y','width','height'].every(k=>Math.abs(actual[k]-parent[k])<.6),`visual and hit bounds match at ${width} / ${height} / ${zoom}: ${JSON.stringify({parent,actual,font:child.font})}`)
   check(child.labels===8,'all native labels and icons remain present')
   const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'定时任务',exact:true}).boundingBox()
   await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();await page.mouse.move(b.x+b.width/2,b.y+b.height/2,{steps:30});await page.waitForTimeout(180)
   check(Number(await dock.locator('.dock-hit-surface').getAttribute('data-lift'))>.7,'pressed lens raises with native foreground')
   await page.mouse.up();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor();await dock.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled')
  }
  await page.getByRole('tab',{name:'定时任务',exact:true}).focus();await page.keyboard.press('ArrowRight');await page.getByRole('heading',{name:'缓存管理',exact:true}).waitFor()
  check(await page.locator('[data-isolated=true]').getAttribute('data-input')==='keyboard','keyboard navigation keeps visible native focus indication')
  await page.getByRole('button',{name:'反馈与帮助',exact:true}).click();const dialog=page.getByRole('dialog',{name:'反馈与帮助',exact:true});await dialog.waitFor();await page.waitForTimeout(250)
  const point=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),hit=await page.evaluate(p=>document.elementFromPoint(p.x,p.y)?.closest('.modal-layer')!==null,{x:point.x+point.width/2,y:point.y+point.height/2})
  check(hit,'modal scrim blocks the navigation hit targets');await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'})
  await dock.locator('.dock-shader-canvas').evaluate(el=>{const ext=el.getContext('webgl2').getExtension('WEBGL_lose_context');window.__restore=()=>ext.restoreContext();ext.loseContext()});await dock.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='false');await page.waitForTimeout(200);await dock.evaluate(()=>window.__restore());await dock.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.shaderReady==='true');check(true,'context loss restores original optical resources')
  const pids=await app.evaluate(()=>global.__isolatedTest.getWindow().webContents.mainFrame.framesInSubtree.map(f=>f.osProcessId));check(new Set(pids).size===2,'main page and navigation have distinct renderer processes')
  const before=await app.evaluate(({app})=>app.getAppMetrics());await app.evaluate(()=>global.__isolatedTest.getWindow().close());let after
  for(let i=0;i<100;i++){after=await app.evaluate(({app})=>app.getAppMetrics());if(!after.some(p=>pids.includes(p.pid)))break;await new Promise(r=>setTimeout(r,50))}
  check(!after.some(p=>pids.includes(p.pid)),'tray releases both renderers');report.memory.push({density,before:before.map(p=>({type:p.type,kb:p.memory.privateBytes})),after:after.map(p=>({type:p.type,kb:p.memory.privateBytes}))})
  const next=app.waitForEvent('window',{timeout:30000});await app.evaluate(()=>global.__isolatedTest.show());page=await next;dock=await ready();check(Boolean(dock),'tray restoration rebuilds independent optics');check(await page.locator('[data-startup-warmup]').count()===0,'no hidden form warmups remain')
 }catch(error){report.errors.push({density,error:error.stack});process.exitCode=1}finally{await app.close()}
}
report.passed=report.errors.length===0;await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))
