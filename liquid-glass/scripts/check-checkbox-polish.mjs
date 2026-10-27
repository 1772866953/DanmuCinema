import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/checkbox-polish');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const report={passed:false,cases:[],errors:[],toggles:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()});await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(200);await page.screenshot({path:path.join(out,'overview.png')})
 const navShadow=await page.locator('.nav-item .glass-material').evaluateAll(els=>els.map(el=>getComputedStyle(el).boxShadow));check(navShadow.every(v=>v==='none'),'导航按钮没有外部投影或灰色底影')
 await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.getByRole('button',{name:'管理接口',exact:true}).click();const dialog=page.getByRole('dialog',{name:'管理接口'});await dialog.waitFor();await page.waitForTimeout(250)
 const checkbox=dialog.getByRole('checkbox',{name:'默认只搜索动漫'}),positions=()=>dialog.evaluate(el=>({list:el.querySelector('.source-list').getBoundingClientRect().toJSON(),field:el.querySelector('.field').getBoundingClientRect().toJSON(),label:el.querySelector('.field>span').getBoundingClientRect().toJSON()}))
 const initial=await positions()
 for(let i=0;i<4;i++){
  await page.evaluate(()=>{window.tickFrames=[];const start=performance.now();function frame(){const g=document.querySelector('.source-list + .check .check-glass'),p=document.querySelector('.source-list + .check .check-glyph path'),f=document.querySelector('.source-list + .check + .field');window.tickFrames.push({scale:new DOMMatrix(getComputedStyle(g).transform).a,stroke:p.getAttribute('stroke-dasharray'),y:f.getBoundingClientRect().top});if(performance.now()-start<450)requestAnimationFrame(frame)}requestAnimationFrame(frame)})
  await checkbox.click();await page.waitForTimeout(480);const current=await positions(),frames=await page.evaluate(()=>window.tickFrames);report.toggles.push({current,frames});check(Math.abs(current.list.y-initial.list.y)<.05&&Math.abs(current.field.y-initial.field.y)<.05&&frames.every(f=>Math.abs(f.y-initial.field.y)<.05),'点击默认动漫选项时，上下内容位置保持固定');check(frames.some(f=>Math.abs(f.scale-1)>.01),'复选框出现玻璃回弹中间帧');check(new Set(frames.map(f=>f.stroke)).size>2,'勾选与取消具有连续笔画过渡')
 }
 check(await dialog.locator('.check-glyph').count()===await dialog.getByRole('checkbox').count(),'全部复选框使用统一玻璃勾选组件');await page.screenshot({path:path.join(out,'sources.png')});await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'})
 await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();const match=page.getByRole('dialog',{name:'选择弹幕来源'});await match.waitFor();await page.waitForTimeout(250)
 const tabs=match.locator('.scope-tabs');for(const name of ['单集','整个季度','已选影片']){await tabs.getByRole('button',{name,exact:true}).click();check(await tabs.getByRole('button',{name,exact:true}).getAttribute('aria-pressed')==='true',`${name}明确标记选中状态`)}
 const colors=await tabs.locator('button').evaluateAll(els=>els.map(el=>({selected:el.getAttribute('aria-pressed'),fill:getComputedStyle(el).getPropertyValue('--material-fill'),color:getComputedStyle(el).color})));report.tabs=colors;check(colors.find(c=>c.selected==='true').fill!==colors.find(c=>c.selected==='false').fill,'选中标签使用独立深青色玻璃背景');await page.screenshot({path:path.join(out,'selected-tabs.png')});await page.keyboard.press('Escape');await match.waitFor({state:'detached'})
 await page.emulateMedia({reducedMotion:'reduce'});await page.getByRole('button',{name:'管理接口',exact:true}).click();await page.waitForTimeout(120);await page.getByRole('checkbox',{name:'默认只搜索动漫'}).click();await page.waitForTimeout(100);check(await page.locator('.source-list + .check .check-glass').evaluate(el=>getComputedStyle(el).transform)==='none','减少动态效果时关闭玻璃回弹');check(report.errors.length===0,'本轮界面没有 JavaScript 异常');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases},null,2))
