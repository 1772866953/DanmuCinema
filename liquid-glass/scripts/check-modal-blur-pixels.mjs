import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),sharp=runtime('sharp'),out=path.resolve('artifacts/modal-blur-pixels-'+Date.now()),report={passed:false,samples:[],errors:[]}
await fs.mkdir(out,{recursive:true});const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,'module.exports=api=>global.__blur=api')
const env={...process.env,DANMU_QA_OFFSCREEN:process.env.DANMU_BLUR_NATIVE==='1'?'0':'1',DANMU_TRAY_CACHE_MS:'20000',DANMU_QA_SCRIPT:hook,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const root=process.env.DANMU_PACKAGED_ROOT,app=await _electron.launch({executablePath:root?path.join(root,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(root?[]:['.']),'--qa'],env,timeout:40000})
const capture=async()=>Buffer.from(await app.evaluate(async()=>{const w=global.__blur.getWindow();return(await w.capturePage()).toPNG().toString('base64')}),'base64')
try{
 let page=await app.firstWindow();page.setDefaultTimeout(15000)
 const prepare=async()=>{
  page.on('pageerror',e=>report.errors.push(e.message));await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
  await page.waitForFunction(()=>!document.querySelector('[data-startup-warmup]')&&['player','sources'].every(name=>document.querySelector(`[data-retained-dialog="${name}"][data-parked="true"]`)))
  for(let n=0;n<160;n++){if(await app.evaluate(()=>global.__blur.getWindow().getOpacity()===1))break;await page.waitForTimeout(50)}
  if(process.env.DANMU_BLUR_NATIVE==='1')await app.evaluate(()=>{const w=global.__blur.getWindow();w.show();w.focus()})
  await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor()
  if(process.env.DANMU_BLUR_CSS)await page.addStyleTag({content:process.env.DANMU_BLUR_CSS})
  await page.evaluate(()=>{const probe=document.createElement('div');probe.id='qa-blur-probe';Object.assign(probe.style,{position:'fixed',inset:'0',zIndex:'190',pointerEvents:'none',backgroundImage:'conic-gradient(#13281f 25%,#f4fff7 0 50%,#13281f 0 75%,#f4fff7 0)',backgroundSize:'8px 8px'});document.querySelector('.workspace').append(probe)})
  await page.waitForTimeout(100)
 }
 const stats=async(buffer,rect)=>{const {data,info}=await sharp(buffer).extract(rect).removeAlpha().raw().toBuffer({resolveWithObject:true});let sum=0,square=0,n=0;for(let i=0;i<data.length;i+=info.channels){const value=(data[i]+data[i+1]+data[i+2])/3;sum+=value;square+=value*value;n++}return{mean:sum/n,std:Math.sqrt(Math.max(0,square/n-(sum/n)**2))}}
 await prepare()
 for(const phase of ['cold','repeat',...(process.env.DANMU_BLUR_TRAY==='1'?['tray']:[])]){
  if(phase==='tray'){
   await app.evaluate(()=>global.__blur.getWindow().close());await new Promise(r=>setTimeout(r,65000));if(await app.evaluate(()=>!!global.__blur.getWindow()))throw Error('tray window did not expire')
   const next=app.waitForEvent('window');await app.evaluate(()=>global.__blur.show());page=await next;await prepare()
  }
  const before=await capture();await page.getByRole('button',{name:'本地播放设置',exact:true}).evaluate(n=>n.click());const dialog=page.getByRole('dialog',{name:'本地播放与弹幕',exact:true});await dialog.waitFor();await page.waitForTimeout(550)
  const buffer=await capture(),meta=await sharp(buffer).metadata(),css=await page.evaluate(()=>({width:innerWidth,height:innerHeight})),box=await dialog.boundingBox(),sx=meta.width/css.width,sy=meta.height/css.height
  const regions={outside:{left:Math.round(12*sx),top:Math.round(css.height*.35*sy),width:Math.round(12*sx),height:Math.round(100*sy)},inside:{left:Math.round((box.x+8)*sx),top:Math.round((box.y+Math.min(200,box.height*.4))*sy),width:Math.round(10*sx),height:Math.round(80*sy)}}
  const values={};for(const [name,rect] of Object.entries(regions)){const original=await stats(before,rect),actual=await stats(buffer,rect);values[name]={original,actual,ratio:actual.std/original.std}}
  const surfaces=await page.evaluate(()=>({source:getComputedStyle(document.querySelector('.workspace')).filter,scrim:getComputedStyle(document.querySelector('.modal-layer:not([inert]) .modal-scrim')).opacity}))
  await fs.writeFile(path.join(out,phase+'.png'),buffer);report.samples.push({phase,...values,surfaces,passed:values.outside.ratio<.15&&values.inside.ratio<.15})
  await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});await page.waitForTimeout(250)
 }
 report.passed=report.samples.every(s=>s.passed)&&!report.errors.length
}catch(error){report.error=error.stack}
finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report},null,2));if(!report.passed)process.exitCode=1}
