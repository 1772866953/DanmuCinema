import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/material-transitions')
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const report={passed:false,cases:[],samples:{},errors:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(10000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 const record=selector=>page.evaluate(selector=>{window.materialFrames=[];const start=performance.now();function frame(){const el=document.querySelector(selector);if(el){const s=getComputedStyle(el),parents=[];for(let p=el.parentElement;p;p=p.parentElement){const c=getComputedStyle(p);if(Number(c.opacity)<1||c.filter!=='none')parents.push({class:p.className,opacity:c.opacity,filter:c.filter})}window.materialFrames.push({t:performance.now()-start,opacity:Number(s.opacity),backdrop:s.backdropFilter,parents,rect:el.getBoundingClientRect().toJSON()})}if(performance.now()-start<700)requestAnimationFrame(frame)}requestAnimationFrame(frame)},selector)
 const verify=async(label)=>{await page.waitForTimeout(750);const frames=await page.evaluate(()=>window.materialFrames);report.samples[label]=frames;check(frames.some(f=>f.opacity>.05&&f.opacity<.95),`${label}：存在连续淡入中间帧`);check(frames.every(f=>f.backdrop!=='none'&&f.parents.length===0),`${label}：玻璃直接采样背景，祖先无透明度或滤镜切换`);check(new Set(frames.filter(f=>f.opacity>.01).map(f=>f.backdrop)).size===1,`${label}：首个可见帧到结束使用同一玻璃滤镜`)}
 await page.locator('.sidebar').getByRole('button',{name:'连接设备',exact:true}).click()
 const select=page.getByRole('combobox',{name:'局域网网卡'})
 for(const label of ['首次下拉','重复下拉']){await record('.select-menu');await select.click();await verify(label);await page.screenshot({path:path.join(out,label+'.png')});await page.keyboard.press('Escape');await page.getByRole('listbox').waitFor({state:'detached'})}
 await page.locator('.sidebar').getByRole('button',{name:'反馈与帮助',exact:true}).click();await page.waitForTimeout(450);await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'})
 await record('.modal-presentation');await page.locator('.sidebar').getByRole('button',{name:'反馈与帮助',exact:true}).click();await verify('反馈弹窗');await page.screenshot({path:path.join(out,'反馈弹窗.png')});await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'})
 await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.locator('tbody tr').first().getByRole('checkbox').click();await page.waitForTimeout(320)
 await page.evaluate(()=>{window.collapseFrames=[];const start=performance.now();function frame(){window.collapseFrames.push(document.querySelector('.media-table').getBoundingClientRect().top);if(performance.now()-start<500)requestAnimationFrame(frame)}requestAnimationFrame(frame)})
 await page.getByRole('button',{name:'取消选择',exact:true}).click();await page.waitForTimeout(550);const collapse=await page.evaluate(()=>window.collapseFrames);report.collapseFrames=collapse;check(new Set(collapse.map(Math.round)).size>5,'取消选择后表格连续收回，无瞬间平移')
 await page.locator('tbody tr').first().getByRole('checkbox').click();await page.waitForTimeout(320);await page.getByRole('button',{name:'匹配已选影片',exact:true}).click();const match=page.getByRole('dialog',{name:'选择弹幕来源'});await match.waitFor();await page.waitForTimeout(450)
 await record('.modal-layer:last-of-type .modal-presentation');await match.getByRole('button',{name:'管理接口',exact:true}).click();await page.waitForTimeout(450);const sources=page.getByRole('dialog',{name:'管理接口'});await sources.getByRole('button',{name:'关闭此页面'}).click();await sources.waitFor({state:'detached'});check(await match.isVisible(),'关闭二级弹窗保留上级弹窗');await page.screenshot({path:path.join(out,'关闭子弹窗后.png')});await page.keyboard.press('Escape');await match.waitFor({state:'detached'})
 await page.locator('.sidebar').getByRole('button',{name:'启动与偏好',exact:true}).click();await page.getByRole('combobox',{name:'界面缩放'}).click();await page.getByRole('option',{name:'120%',exact:true}).click();await page.getByRole('button',{name:'保存偏好',exact:true}).click();await page.waitForTimeout(300)
 await record('.select-menu');await page.getByRole('combobox',{name:'界面缩放'}).click();await verify('120%首次下拉');await page.screenshot({path:path.join(out,'120%-下拉.png')});await page.keyboard.press('Escape')
 await page.emulateMedia({reducedMotion:'reduce'});await page.locator('.sidebar').getByRole('button',{name:'反馈与帮助',exact:true}).click();await page.waitForTimeout(150);check(await page.getByRole('dialog').evaluate(el=>getComputedStyle(el).transform)==='none','减少动态效果下取消弹窗缩放与位移');await page.keyboard.press('Escape')
 check(report.errors.length===0,'本轮相关页面无 JavaScript 异常');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases},null,2))
