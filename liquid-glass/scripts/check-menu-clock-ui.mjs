import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/menu-clock-ui-'+Date.now()),report={passed:false,checks:[],layouts:[],errors:[]}
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_QA_OFFSCREEN:'1',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const root=process.env.DANMU_PACKAGED_ROOT
const app=await _electron.launch({executablePath:root?path.join(root,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(root?[]:['.']),'--qa'],env,timeout:40000})
const check=(value,text)=>{assert(value,text);report.checks.push(text)}
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message))
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 for(const [width,height,zoom] of [[1320,880,1],[940,640,1],[1320,880,1.2],[940,640,1.2]]){
  await app.evaluate(({BrowserWindow},v)=>{const w=BrowserWindow.getAllWindows()[0];w.setContentSize(v.width,v.height);w.webContents.setZoomFactor(v.zoom)},{width,height,zoom})
  await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor()
  for(const [edgeX,edgeY] of [[2,2],[-2,-2],[2,-2],[-2,2]]){
   await page.locator('.poster-card').first().evaluate((n,{edgeX,edgeY})=>n.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:edgeX<0?innerWidth+edgeX:edgeX,clientY:edgeY<0?innerHeight+edgeY:edgeY})),{edgeX,edgeY})
   const menu=page.getByRole('menu',{name:'影片操作'});await menu.waitFor();await page.waitForTimeout(240)
   const box=await menu.evaluate(n=>{const r=n.getBoundingClientRect(),last=n.lastElementChild.getBoundingClientRect(),hit=document.elementFromPoint(last.left+last.width/2,last.top+last.height/2);return{x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:innerWidth,height:innerHeight,lastVisible:last.bottom<=r.bottom&&n.contains(hit),parent:n.parentElement.tagName,background:getComputedStyle(n).backgroundColor}})
   report.menus??=[];report.menus.push({width,height,zoom,edgeX,edgeY,...box})
   check(box.x>=11&&box.y>=11&&box.right<=box.width-11&&box.bottom<=box.height-11&&box.lastVisible,`${width}×${height}/${zoom}: menu at ${edgeX},${edgeY} keeps all five commands visible and clickable`)
   check(box.parent==='BODY','media menu escapes the list clipping and bottom fade')
   await page.keyboard.press('Escape');await menu.waitFor({state:'detached'})
  }
  await page.getByRole('tab',{name:'定时任务',exact:true}).click()
  for(const [mode,name] of [['at','指定时间'],['weekly','每周计划'],['delay','倒计时']]){
   await page.getByRole('combobox',{name:'计时方式',exact:true}).click();await page.getByRole('option',{name,exact:true}).click()
   const picker=page.locator('.duration-picker');await picker.waitFor();await page.waitForTimeout(180)
   const layout=await picker.evaluate(n=>{const r=n.getBoundingClientRect(),panel=n.closest('.panel').getBoundingClientRect(),fields=[...document.querySelectorAll('.schedule-fields input,.schedule-fields .duration-picker,.schedule-fields .week-days')].map(n=>n.getBoundingClientRect());return{digits:getComputedStyle(n.querySelector('.duration-option')).fontSize,columns:n.querySelectorAll('[role=spinbutton]').length,nativeTime:document.querySelectorAll('input[type=time]').length,bottom:r.bottom,limit:panel.bottom,overflow:document.documentElement.scrollWidth>innerWidth,fieldsVisible:fields.every(r=>r.left>=0&&r.right<=innerWidth&&r.bottom<=Math.min(innerHeight,panel.bottom)+1),sizes:['.app-shell','.workspace','.page-scene','.page-content','.schedule-grid','.schedule-fields'].map(s=>{const n=document.querySelector(s),r=n.getBoundingClientRect();return{s,width:r.width,x:r.x,zoom:getComputedStyle(n).zoom}}),fields:fields.map(r=>({x:r.x,right:r.right,bottom:r.bottom})),viewport:[innerWidth,innerHeight]}})
   report.layouts.push({width,height,zoom,mode,...layout})
   check(layout.columns===3&&layout.nativeTime===0&&parseFloat(layout.digits)<=17&&!layout.overflow&&layout.fieldsVisible,`${width}×${height}/${zoom}: ${name} uses smaller glass wheels with no clipped controls`)
   const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,`${mode}-${width}-${height}-${zoom}.png`),Buffer.from(png,'base64'))
  }
 }
 await page.getByRole('combobox',{name:'计时方式',exact:true}).click();await page.getByRole('option',{name:'每周计划',exact:true}).click()
 const minutes=page.getByRole('spinbutton',{name:'执行时间分钟',exact:true}),initial=Number(await minutes.getAttribute('aria-valuenow'))
 await minutes.focus();await minutes.press(initial<59?'ArrowDown':'ArrowUp');await page.waitForFunction(v=>Number(document.querySelector('[aria-label="执行时间分钟"]').getAttribute('aria-valuenow'))!==v,initial)
 const chosen=await minutes.getAttribute('aria-valuenow');await page.getByRole('tab',{name:'启动与偏好',exact:true}).click();await page.getByRole('tab',{name:'定时任务',exact:true}).click()
 check(await minutes.getAttribute('aria-valuenow')===chosen,'clock-wheel edits survive page changes')
 check(!report.errors.length,'no renderer errors');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1;try{await (await app.firstWindow()).screenshot({path:path.join(out,'failure.png')})}catch{}}
finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,passed:report.passed,checks:report.checks.length,error:report.error},null,2))}
