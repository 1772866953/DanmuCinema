import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),before=process.argv.includes('--before'),out=path.resolve('artifacts/rendering-artifacts',before?'before':'after'),report={passed:false,cases:[],errors:[],rects:[]}
await fs.mkdir(path.join(out,'recording'),{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const check=(v,n)=>{assert(v,n);report.cases.push(n)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1320,880);w.show();w.focus()});await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(550)
 const hero=page.locator('.hero'),rail=page.locator('.dock-hit-surface'),frames=[],writes=[],cdp=await page.context().newCDPSession(page);let number=0
 cdp.on('Page.screencastFrame',e=>{const file=String(number++).padStart(5,'0')+'.jpg';frames.push({file,time:e.metadata.timestamp});writes.push(fs.writeFile(path.join(out,'recording',file),Buffer.from(e.data,'base64')));cdp.send('Page.screencastFrameAck',{sessionId:e.sessionId}).catch(()=>{})})
 await cdp.send('Page.startScreencast',{format:'jpeg',quality:96,maxWidth:1800,maxHeight:1200,everyNthFrame:1})
 const capture=async name=>{const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'))}
 for(const [width,height,scale] of [[1320,880,100],[1320,880,120],[1040,760,100]]){
  await app.evaluate(({BrowserWindow},wh)=>BrowserWindow.getAllWindows()[0].setSize(...wh),[width,height]);await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.waitForTimeout(400)
  const h=await hero.boundingBox(),viewport=await page.evaluate(()=>({width:innerWidth,height:innerHeight}));report.rects.push({firstFrame:frames.length,hero:h,viewport,scale,width,height})
  for(let cycle=0;cycle<3;cycle++){
   const first=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),other=await page.getByRole('tab',{name:cycle%2?'连接设备':'运行日志',exact:true}).boundingBox()
   await page.mouse.move(first.x+first.width/2,first.y+first.height/2);await page.mouse.down();await page.waitForTimeout(140)
   await page.mouse.move(other.x+other.width/2,other.y+other.height/2-5,{steps:30});await page.waitForTimeout(130)
   check(await rail.getAttribute('data-held')==='true',`${width}/${scale}/${cycle}: continuous drag remains captured`)
   if(cycle===1)await capture(`${width}-${scale}-held-connect`)
   await page.mouse.move(first.x+first.width/2,first.y+first.height/2,{steps:26});await page.mouse.up();await page.waitForTimeout(550)
   check(await page.locator('.page-heading h1').textContent()==='服务总览',`${width}/${scale}/${cycle}: stress stays on overview`)
  }
  await capture(`${width}-${scale}-rest`)
 }
 await cdp.send('Page.stopScreencast');await Promise.all(writes);await fs.writeFile(path.join(out,'recording/frames.json'),JSON.stringify(frames,null,2))
 if(!before){check(await page.locator('.library-optics').count()===0,'overview no longer uses the large backdrop SVG filter');check(await page.locator('.dock-recording .dock-lens-veil,.dock-recording .dock-raised-rim').count()===0,'recorded input excludes the raised lens itself');check(await page.locator('.dock-recording').count()===2&&await page.locator('.dock-recording .dock-source-rail').count()===2,'native centre and optical rim share matching recorded rail sources');check(await page.locator('.panel-optic-input').evaluate(el=>getComputedStyle(el).backdropFilter==='none'&&getComputedStyle(el).backgroundColor==='rgb(168, 187, 177)'),'panel input is opaque and independent of sibling backdrop filters')}
 check(report.errors.length===0,'stress capture has no renderer errors');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,out}))
