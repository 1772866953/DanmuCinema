import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/isolated-dock-'+Date.now()),report={passed:false,errors:[],console:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ISOLATED_DOCK:'1',DANMU_EXPERIMENTAL_DOCK_SHADER:'1',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--enable-features=SkiaGraphite'],env,timeout:40000})
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}))
 await page.waitForFunction(()=>document.querySelector('[data-isolated=true]')?.dataset.ready==='true',{timeout:25000})
 report.frames=await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.mainFrame.framesInSubtree.map(f=>({url:f.url,pid:f.osProcessId})))
 assert(new Set(report.frames.map(f=>f.pid)).size===2,'navigation must actually run in a different renderer')
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 const dock=page.frames().find(f=>f.url().startsWith('cinema-dock:'));assert(dock)
 report.pose=await dock.locator('.dock-hit-surface').evaluate(el=>({...el.dataset,viewport:[innerWidth,innerHeight],bounds:el.getBoundingClientRect().toJSON()}))
 const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'定时任务',exact:true}).boundingBox()
 await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();await page.mouse.move(b.x+b.width/2,b.y+b.height/2,{steps:50});await page.waitForTimeout(250)
 report.held=await dock.locator('.dock-hit-surface').evaluate(el=>({...el.dataset}));assert(Number(report.held.lift)>.8)
 await fs.writeFile(path.join(out,'held.png'),Buffer.from(await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64')),'base64'))
 await page.mouse.up();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor();assert.equal(report.errors.length,0);report.passed=true
}catch(error){report.errors.push(error.stack);const page=await app.firstWindow();report.frameStates=await Promise.all(page.frames().map(async f=>({url:f.url(),state:await f.evaluate(()=>({body:document.body.innerText.slice(0,200),dock:document.querySelector('.dock-hit-surface')?.getAttribute('data-ready')})).catch(()=>null)})));process.exitCode=1}finally{await app.close();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}
