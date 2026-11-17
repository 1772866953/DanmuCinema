import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/duration-wheel-'+Date.now())
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_QA_OFFSCREEN:'1',DANMU_QA_FIXTURE:path.resolve('tests/fixture.cjs'),DANMU_ELECTRON_USER_DATA:path.join(out,'profile')};delete env.ELECTRON_RUN_AS_NODE
const packaged=process.env.DANMU_PACKAGED_ROOT
const app=await _electron.launch({executablePath:packaged?path.join(packaged,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(packaged?[]:['.']),'--qa'],env,timeout:40000})
const report={passed:false,checks:[],errors:[]},check=(value,text)=>{assert(value,text);report.checks.push(text)}
try{
 const page=await app.firstWindow();page.on('pageerror',error=>report.errors.push(error.message))
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await page.getByRole('tab',{name:'定时任务',exact:true}).click()
 const hours=page.getByRole('spinbutton',{name:'倒计时小时',exact:true}),minutes=page.getByRole('spinbutton',{name:'倒计时分钟',exact:true}),seconds=page.getByRole('spinbutton',{name:'倒计时秒',exact:true})
 const settle=async()=>{await page.evaluate(()=>{window.__wheelStable=null});await page.waitForFunction(()=>{const nodes=[...document.querySelectorAll('.duration-wheel')],signature=nodes.map(n=>n.scrollTop).join(','),now=performance.now();if(window.__wheelStable?.signature!==signature){window.__wheelStable={signature,since:now};return false}return now-window.__wheelStable.since>180&&nodes.every(n=>Math.abs(n.scrollTop-Number(n.getAttribute('aria-valuenow'))*n.firstElementChild.offsetHeight)<.6)})}
 const value=async()=>Promise.all([hours,minutes,seconds].map(node=>node.getAttribute('aria-valuenow').then(Number)))
 await hours.waitFor();await settle();check(JSON.stringify(await value())==='[0,30,0]','existing 30-minute default appears at the center of the three wheels')
 await minutes.focus();await minutes.press('ArrowDown');await settle();check((await value())[1]===31,'keyboard arrows change the selected minute and snap to a full row')
 await minutes.press('ArrowDown');await minutes.press('ArrowDown');await minutes.press('ArrowDown');await settle();check((await value())[1]===34,'rapid consecutive arrow presses accumulate without losing input during settling')
 const position=await seconds.boundingBox();await page.mouse.move(position.x+position.width/2,position.y+position.height/2);await page.mouse.wheel(0,92)
 await page.waitForTimeout(500);await settle();check((await value())[2]>0,'native mouse wheel selects seconds without scrolling the surrounding page')
 await hours.focus();await hours.press('2');await settle();check((await value())[0]===2,'digits on a focused wheel support direct hour entry')
 await page.getByRole('button',{name:'15 分钟',exact:true}).click();await settle();check(JSON.stringify(await value())==='[0,15,0]','a preset updates all columns and its value survives the settling animation')
 const drag=await minutes.boundingBox();await page.mouse.move(drag.x+drag.width/2,drag.y+drag.height/2);await page.mouse.down();await page.mouse.move(drag.x+drag.width/2,drag.y+drag.height/2-42,{steps:8});await page.mouse.up();await page.waitForTimeout(600);await settle();check((await value())[1]>15,'mouse dragging advances the wheel and settles to a complete row')
 await seconds.focus();await seconds.press('End');await settle();check((await value())[2]===59,'seconds stop at 59')
 const selected=await value();await page.getByRole('tab',{name:'启动与偏好',exact:true}).click();await page.getByRole('tab',{name:'定时任务',exact:true}).click();await settle();check(JSON.stringify(await value())===JSON.stringify(selected),'switching pages preserves the complete hour/minute/second selection')
 await page.getByRole('button',{name:'开始定时',exact:true}).click();check((await page.getByRole('dialog').innerText()).includes('59 秒'),'confirmation uses seconds rather than fractional minutes');await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'})
 await hours.focus();await hours.press('End');await settle();check(JSON.stringify(await value())==='[720,0,0]','30-day limit is retained without overflowing minutes or seconds')
 check(await minutes.getAttribute('aria-valuemax')==='0'&&await seconds.getAttribute('aria-valuemax')==='0','remaining columns cannot exceed the maximum duration')
 for(const node of [hours,minutes,seconds]){await node.focus();await node.press('Home')};await settle();check(await page.getByRole('button',{name:'开始定时',exact:true}).isDisabled(),'zero duration cannot start an invalid plan')
 await page.getByRole('button',{name:'30 分钟',exact:true}).click();await settle()
 const restore=plan=>page.evaluate(async plan=>{const state=await window.cinema.call('state');window.__qa.setState({...state,schedule:plan})},plan)
 await restore({id:'wheel-active',active:true,mode:'delay',seconds:3661,remaining:'00:50:00',status:'测试计划',action:0})
 await page.waitForFunction(()=>document.querySelector('[aria-label="倒计时小时"]').getAttribute('aria-valuenow')==='1');await settle()
 check(JSON.stringify(await value())==='[1,1,1]','restored system plans retain second precision instead of showing remaining progress as the duration')
 check((await hours.getAttribute('aria-disabled'))==='true'&&(await minutes.getAttribute('tabindex'))==='-1','active plans disable wheel interaction and tab stops')
 await restore({active:false,remaining:'00:00:00',status:'尚未设置定时任务'})
 await page.getByRole('button',{name:'30 分钟',exact:true}).click();await settle()
 for(const [width,height,zoom] of [[1320,880,1],[940,640,1],[1320,880,1.2],[940,640,1.2]]){
  await app.evaluate(({BrowserWindow},{width,height,zoom})=>{const w=BrowserWindow.getAllWindows()[0];w.setContentSize(width,height);w.webContents.setZoomFactor(zoom)},{width,height,zoom});await page.waitForTimeout(200);await settle()
  const layout=await page.evaluate(()=>{const wheel=document.querySelector('.duration-picker'),check=document.querySelector('.schedule-fields>.check'),panel=wheel.closest('.panel'),r=wheel.getBoundingClientRect(),c=check.getBoundingClientRect(),p=panel.getBoundingClientRect();return{width:r.width,bottom:c.bottom,limit:Math.min(p.bottom,innerHeight),overflow:document.documentElement.scrollWidth>innerWidth,aligned:[...document.querySelectorAll('.duration-wheel')].every(n=>Math.abs(n.scrollTop-Number(n.getAttribute('aria-valuenow'))*n.firstElementChild.offsetHeight)<.6)}})
  report.layouts??=[];report.layouts.push({windowWidth:width,windowHeight:height,zoom,...layout})
  const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,`schedule-${width}-${height}-${zoom}.png`),Buffer.from(png,'base64'))
  check(layout.width>200&&!layout.overflow&&layout.bottom<=layout.limit+1&&layout.aligned,`${width}×${height} at ${zoom*100}% retains visible controls and centered rows`)
 }
 await page.emulateMedia({reducedMotion:'reduce'});await minutes.focus();await minutes.press('ArrowDown');await settle();check((await value())[1]===31,'reduced-motion mode preserves keyboard selection and snapping')
 check(!report.errors.length,'no renderer errors');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1;console.error(error);try{const page=await app.firstWindow();report.wheels=await page.locator('.duration-wheel').evaluateAll(nodes=>nodes.map(n=>({now:n.getAttribute('aria-valuenow'),top:n.scrollTop,height:n.clientHeight,row:n.firstElementChild.offsetHeight})));await page.screenshot({path:path.join(out,'failure.png')})}catch{}}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({out,...report},null,2))
