import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/tray-graphics-memory-'+Date.now()),report={variants:[]},sleep=ms=>new Promise(r=>setTimeout(r,ms))
await fs.mkdir(out,{recursive:true})
for(const pressure of [false,true]){
 const profile=path.join(out,'profile-'+pressure),hook=path.join(out,'hook.cjs');await fs.mkdir(profile,{recursive:true});await fs.writeFile(hook,'module.exports=api=>global.__memoryTest=api')
 const env={...process.env,DANMU_ISOLATED_DOCK:'1',DANMU_EXPERIMENTAL_DOCK_SHADER:'1',DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_SCRIPT:hook,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
 const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--enable-features=SkiaGraphite','--force-device-scale-factor=1'],env,timeout:40000}),item={pressure,samples:[]}
 const sample=async label=>item.samples.push({label,processes:await app.evaluate(({app})=>app.getAppMetrics().map(p=>({pid:p.pid,type:p.type,kb:p.memory.privateBytes})))})
 try{
  let page=await app.firstWindow();await page.waitForFunction(()=>document.querySelector('[data-isolated=true]')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__memoryTest.getWindow();w.show();w.focus()});await sample('initial-ready')
  for(const [width,height,zoom]of [[940,640,1.2],[1320,880,1.2],[1320,880,1]]){await app.evaluate((_,g)=>{const w=global.__memoryTest.getWindow();w.webContents.setZoomFactor(g.zoom);w.setContentSize(g.width,g.height)},{width,height,zoom});await sleep(700)}
  await sample('before-tray');if(pressure){const cdp=await page.context().newCDPSession(page);await cdp.send('Memory.simulatePressureNotification',{level:'critical'});await cdp.detach();await sample('after-pressure')}
  await app.evaluate(()=>global.__memoryTest.getWindow().close());await sleep(500);await sample('tray-500ms');await sleep(4500);await sample('tray-5s');await sleep(15000);await sample('tray-20s')
  const next=app.waitForEvent('window',{timeout:30000}),at=Date.now();await app.evaluate(()=>global.__memoryTest.show());page=await next;await page.waitForFunction(()=>document.querySelector('[data-isolated=true]')?.dataset.ready==='true');item.restoreMs=Date.now()-at;await sample('restored')
 }catch(error){item.error=error.stack;process.exitCode=1}finally{await app.close();report.variants.push(item);await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2))}
}
console.log(JSON.stringify({out,...report},null,2))
