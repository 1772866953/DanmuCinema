import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/thumbnail-cache'),report={passed:false,cases:[],errors:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 // Only synthetic thumbnails and loading events. No media server or API calls.
 await app.evaluate(async({ipcMain,BrowserWindow},project)=>{
  const fixture=process.mainModule.require(project+'/tests/fixture.cjs')()
  const stats=globalThis.__thumbnailTest={requests:0,generation:0,loading:false,keys:[]}
  const snap=()=>({...fixture.snap(),loading:stats.loading}),publish=()=>BrowserWindow.getAllWindows()[0]?.webContents.send('core:state',snap())
  ipcMain.removeHandler('core:call');ipcMain.handle('core:call',async(_event,method,p={})=>{
   if(method==='state')return snap()
   if(method==='cover'){
    stats.requests++;stats.keys.push(p.key);const generation=stats.generation
    await new Promise(resolve=>setTimeout(resolve,180))
    const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="100" height="150"><rect width="100" height="150" fill="${['#467b9c','#ab6259','#657c44'][generation%3]}"/><circle cx="50" cy="50" r="30" fill="#e2d8b0"/><path d="M0 140L100 70V150H0Z" fill="#1e3e51"/><text x="20" y="120" font-size="28" fill="white">${generation}</text></svg>`
    return 'data:image/svg+xml;base64,'+Buffer.from(svg).toString('base64')
   }
   if(method==='scan'||method==='refresh'){
    stats.loading=true;publish();setTimeout(()=>{stats.generation++;stats.loading=false;publish()},650);return true
   }
   const result=await fixture.call(method,p);publish();return result
  })
  const window=BrowserWindow.getAllWindows()[0];window.setSize(1320,880);window.show();window.focus()
 },path.resolve('.'))
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 const stats=()=>app.evaluate(()=>globalThis.__thumbnailTest),images=page.locator('.media-name .file-icon img')
 const go=async label=>{await page.getByRole('tab',{name:label,exact:true}).click();await page.getByRole('heading',{name:label,exact:true}).waitFor();await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled');await page.waitForTimeout(300)}
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await go('影片与弹幕')
 await page.waitForFunction(()=>{const imgs=[...document.querySelectorAll('.media-name .file-icon img')];return imgs.length===3&&imgs.every(i=>i.complete&&i.naturalWidth>0)})
 check((await stats()).requests===3,'first visit requests and decodes each cover once')
 const initial=await images.evaluateAll(imgs=>imgs.map(i=>i.src))
 await page.evaluate(()=>{
  window.__coverFrames=[];window.__captureCoverFrames=true
  const frame=()=>{if(!window.__captureCoverFrames)return;if(document.querySelector('.media-table')){const icons=[...document.querySelectorAll('.media-name .file-icon')];window.__coverFrames.push(icons.map(icon=>{const image=icon.querySelector('img');return Boolean(image?.complete&&image.naturalWidth>0)}))}requestAnimationFrame(frame)};requestAnimationFrame(frame)
 })
 for(let i=0;i<3;i++){await go(i%2?'缓存管理':'服务总览');await go('影片与弹幕')}
 check((await stats()).requests===3,'repeated page switching never requests covers again')
 check(JSON.stringify(await images.evaluateAll(imgs=>imgs.map(i=>i.src)))===JSON.stringify(initial),'reentry retains exact cached thumbnails')
 await page.waitForTimeout(4200)
 check((await stats()).requests===3,'ordinary state polling leaves thumbnails cached')
 for(const [name,generation]of [['刷新媒体库列表',1],['扫描媒体库',2]]){
  const previous=await images.evaluateAll(imgs=>imgs.map(i=>i.src));await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(200)
  check(JSON.stringify(await images.evaluateAll(imgs=>imgs.map(i=>i.src)))===JSON.stringify(previous),name+' keeps covers visible while the library is loading')
  await page.waitForFunction(old=>{const imgs=[...document.querySelectorAll('.media-name .file-icon img')];return imgs.length===3&&imgs.every((image,i)=>image.complete&&image.naturalWidth>0&&image.src!==old[i])},previous)
  check((await stats()).requests===3*(generation+1),name+' refreshes thumbnails once after loading completes')
 }
 const frames=await page.evaluate(()=>{window.__captureCoverFrames=false;return window.__coverFrames})
 check(frames.length>120&&frames.every(frame=>frame.length===3&&frame.every(Boolean)),'every sampled library frame has all three decoded covers, without icon placeholders')
 report.sampledLibraryFrames=frames.length
 const nativeCentre=await page.locator('.dock-native-source').evaluate(el=>getComputedStyle(el).filter==='none'&&getComputedStyle(el).backdropFilter==='none')
 check(nativeCentre,'reference dock keeps native centre text outside the optical filter')
 for(const scale of [100,120]){
  await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.waitForTimeout(400)
  const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,`library-${scale}.png`),Buffer.from(png,'base64'))
 }
 const a=await page.getByRole('tab',{name:'影片与弹幕',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'连接设备',exact:true}).boundingBox()
 await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();await page.mouse.move(b.x+b.width/2,b.y+b.height/2,{steps:20});await page.waitForTimeout(200)
 check(await page.locator('.dock-hit-surface').getAttribute('data-held')==='true','shared button outline preserves captured lens dragging')
 const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,'dock-held.png'),Buffer.from(png,'base64'))
 await page.mouse.up();await page.getByRole('heading',{name:'连接设备',exact:true}).waitFor()
 check(report.errors.length===0,'thumbnail and outline changes have no renderer errors');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,sampledLibraryFrames:report.sampledLibraryFrames}))
