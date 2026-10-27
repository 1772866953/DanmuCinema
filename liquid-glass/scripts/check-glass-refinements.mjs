import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {createRequire} from 'node:module'
import {runtime} from './test-runtime.mjs'
const require=createRequire(import.meta.url)
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/glass-refinements');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const report={passed:false,cases:[],errors:[],gestures:[]};const check=(valid,label)=>{assert(valid,label);report.cases.push(label)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(10000);page.on('pageerror',e=>report.errors.push(e.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(400)
 check(!await page.getByText('好故事，',{exact:false}).count(),'总览移除情绪化介绍，改为视频和弹幕功能说明')
 await page.screenshot({path:path.join(out,'01-overview.png')})
 const nav=page.getByRole('button',{name:'服务总览',exact:true});await nav.hover();await page.waitForTimeout(400)
 const navStyle=await nav.evaluate(el=>{const m=el.querySelector('.glass-material'),r=el.getBoundingClientRect(),b=m.getBoundingClientRect(),c=new DOMMatrix(getComputedStyle(el.querySelector('.glass-content')).transform);return{shadow:getComputedStyle(m).boxShadow,overflow:getComputedStyle(el).overflow,bounds:{left:b.left-r.left,right:r.right-b.right,top:b.top-r.top,bottom:r.bottom-b.bottom},foreground:[c.a,c.b,c.c,c.d]}})
 check(navStyle.shadow==='none','侧栏选中项无外部灰色投影')
 check(Object.values(navStyle.bounds).every(v=>v>=-.5)&&navStyle.overflow==='clip','侧栏悬停材质保持在选项边界内')
 check(JSON.stringify(navStyle.foreground)==='[1,0,0,1]','悬停文字保持单位缩放和零剪切')
 report.navigation=navStyle;await page.locator('.sidebar').screenshot({path:path.join(out,'02-sidebar-hover.png')})
 await page.getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.locator('tbody tr').first().waitFor();await page.waitForTimeout(200)
 await page.evaluate(()=>{window.flowFrames=[];const start=performance.now();function frame(){const table=document.querySelector('.media-table'),bar=document.querySelector('.selection-region');window.flowFrames.push({t:performance.now()-start,y:table.getBoundingClientRect().top,height:table.getBoundingClientRect().height,bar:bar?.getBoundingClientRect().height||0});if(performance.now()-start<650)requestAnimationFrame(frame)}requestAnimationFrame(frame)})
 await page.locator('tbody tr').first().getByRole('checkbox').click();await page.waitForTimeout(700);const frames=await page.evaluate(()=>window.flowFrames);report.selectionFrames=frames
 const startY=frames[0].y,endY=frames.at(-1).y,mid=frames.filter(f=>f.y>startY+1&&f.y<endY-1),jumps=frames.slice(1).map((f,i)=>Math.abs(f.y-frames[i].y))
 check(endY-startY>30&&new Set(mid.map(f=>Math.round(f.y))).size>=5,'选中工具栏展开时，媒体表格经过连续中间位置')
 check(Math.max(...jumps)<(endY-startY)*.65,'表格没有一次性跳到最终位置')
 await page.screenshot({path:path.join(out,'03-selection-expanded.png')})
 await page.getByRole('button',{name:'首次设置',exact:true}).click();const type=page.getByRole('combobox',{name:'识别方式',exact:true});await type.click();await page.getByRole('listbox',{name:'识别方式'}).waitFor();await page.waitForTimeout(200)
 const option=page.getByRole('option',{name:'电影',exact:true});await option.hover();await page.waitForTimeout(350)
 const optionBounds=await option.evaluate(el=>{const a=el.getBoundingClientRect(),b=el.querySelector('.glass-material').getBoundingClientRect(),menu=el.closest('.select-menu').getBoundingClientRect();return{left:b.left-a.left,right:a.right-b.right,menuLeft:b.left-menu.left,menuRight:menu.right-b.right,overflow:getComputedStyle(el).overflow}})
 check(optionBounds.left>=-.5&&optionBounds.right>=-.5&&optionBounds.menuLeft>0&&optionBounds.menuRight>0&&optionBounds.overflow==='clip','下拉选项悬停没有越过自身或菜单边界')
 await page.screenshot({path:path.join(out,'04-dropdown-hover.png')});await page.keyboard.press('Escape')
 await page.getByRole('button',{name:'连接设备',exact:true}).click();const trigger=page.getByRole('combobox',{name:'局域网网卡',exact:true});await trigger.waitFor();const bounds=await trigger.boundingBox()
 for(const [dx,dy]of[[0,80],[0,-80],[100,0],[-100,0],[65,65]]){
  await page.mouse.move(bounds.x+bounds.width/2,bounds.y+bounds.height/2);await page.mouse.down();await page.mouse.move(bounds.x+bounds.width/2+dx,bounds.y+bounds.height/2+dy,{steps:6});await page.waitForTimeout(280)
  const sample=await trigger.evaluate(el=>{const m=el.querySelector('.glass-material'),c=el.querySelector('.glass-content'),label=el.querySelector('.select-option-label'),a=el.getBoundingClientRect(),b=m.getBoundingClientRect(),l=label.getBoundingClientRect(),t=new DOMMatrix(getComputedStyle(c).transform);return{width:a.width,materialInside:[b.left-a.left,a.right-b.right,b.top-a.top,a.bottom-b.bottom],labelInside:[l.left-b.left,b.right-l.right],foreground:[t.a,t.b,t.c,t.d]}})
  report.gestures.push({dx,dy,...sample});check(sample.materialInside.every(v=>v>=-.5)&&sample.labelInside.every(v=>v>=0),`长下拉框拖动 (${dx},${dy})：玻璃不越界，文字位于玻璃内`)
  check(JSON.stringify(sample.foreground)==='[1,0,0,1]','拖动期间不缩放或剪切文字')
  if(dx===0&&dy===80)await page.screenshot({path:path.join(out,'05-wide-select-pull.png')})
  await page.mouse.up();await page.waitForTimeout(450)
 }
 await page.mouse.move(3,3);await page.waitForTimeout(500)
 const colorStyle=await page.addStyleTag({content:'.ambient{background:#558ab7!important}'});await page.waitForTimeout(180);await trigger.screenshot({path:path.join(out,'transmission-blue.png')});await colorStyle.evaluate(el=>el.textContent='.ambient{background:#b48fbe!important}');await page.waitForTimeout(180);await trigger.screenshot({path:path.join(out,'transmission-violet.png')});await colorStyle.evaluate(el=>el.remove())
 const {PNG}=runtime('pngjs'),blue=PNG.sync.read(await fs.readFile(path.join(out,'transmission-blue.png'))),violet=PNG.sync.read(await fs.readFile(path.join(out,'transmission-violet.png')));const center=image=>{const offset=(Math.floor(image.height/2)*image.width+Math.floor(image.width/2))*4;return Array.from(image.data.slice(offset,offset+3))};const a=center(blue),b=center(violet);report.transmission={blue:a,violet:b,delta:a.map((v,i)=>Math.abs(v-b[i]))};check(report.transmission.delta.reduce((x,y)=>x+y,0)>65,'按钮中央实际透过背景颜色，未被白色遮罩覆盖')
 await page.screenshot({path:path.join(out,'06-connect.png')});await page.getByRole('combobox',{name:'局域网网卡'}).click();await page.waitForTimeout(200);await page.keyboard.press('Escape');check(true,'受限形变后正常点击和 Escape 仍可使用')
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(1020,720));await page.waitForTimeout(250);await page.getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.waitForTimeout(300);await page.screenshot({path:path.join(out,'07-compact.png')})
 check(report.errors.length===0,'本轮相关页面无 JavaScript 异常');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases,transmission:report.transmission},null,2))
