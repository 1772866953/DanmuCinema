import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/gesture-matching'),profile=path.join(out,'profile-'+Date.now())
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile};delete env.ELECTRON_RUN_AS_NODE
const dpi=process.argv.includes('--dpi=1.5')?'1.5':'1.25'
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${dpi}`],env})
const report={passed:false,dpi,cases:[],gestures:[],errors:[]},check=(valid,label)=>{assert(valid,label);report.cases.push(label)}
try {
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 async function pull(locator,label,shot=false){
  await locator.scrollIntoViewIfNeeded();await page.mouse.move(2,2);await page.waitForTimeout(300)
  const box=await locator.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();await page.mouse.move(box.x+box.width/2+75,box.y+box.height/2-75,{steps:7});await page.waitForTimeout(300)
  const sample=await locator.evaluate(el=>{const material=el.querySelector(':scope>.glass-material'),content=el.querySelector(':scope>.glass-content'),m=new DOMMatrix(getComputedStyle(material).transform),c=new DOMMatrix(getComputedStyle(content).transform),r=material.getBoundingClientRect();const clipped=[];for(let a=el.parentElement;a;a=a.parentElement){const s=getComputedStyle(a),b=a.getBoundingClientRect();if(['auto','scroll','hidden','clip'].includes(s.overflowY)&&r.top<b.top-(s.overflowY==='clip'?(parseFloat(s.overflowClipMargin)||0):0)-.5)clipped.push(a.className)}return{x:m.e,y:m.f,text:[c.a,c.b,c.c,c.d],clipped}})
  report.gestures.push({label,...sample});check(sample.x>8&&sample.y< -7,`${label} 使用统一大幅位移`);check(JSON.stringify(sample.text)==='[1,0,0,1]',`${label} 文字不缩放或倾斜`);check(sample.clipped.length===0,`${label} 上方形变未被容器裁切`)
  if(shot)await page.screenshot({path:path.join(out,`${label}-${dpi}.png`)})
  await page.mouse.up();await page.mouse.move(2,2);await page.waitForTimeout(450)
 }
 await page.locator('.sidebar').getByRole('button',{name:'下载任务',exact:true}).click()
 await pull(page.getByRole('button',{name:'重试失败项',exact:true}),'重试按钮',true)
 await pull(page.locator('.sidebar').getByRole('button',{name:'连接设备',exact:true}),'导航按钮')
 const trigger=page.getByRole('combobox',{name:'筛选任务'});await pull(trigger,'下拉触发器')
 await trigger.click();await page.getByRole('listbox',{name:'筛选任务'}).waitFor();await pull(page.getByRole('option',{name:'全部任务',exact:true}),'下拉选项',true)
 check(await trigger.getAttribute('aria-expanded')==='true','拖动下拉选项不误触选择或关闭')
 await page.keyboard.press('Escape')
 await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.locator('tbody tr').first().getByRole('checkbox').click()
 await pull(page.getByRole('button',{name:'匹配已选影片',exact:true}),'批量工具栏',true)
 await page.getByRole('button',{name:'匹配已选影片',exact:true}).click()
 const dialog=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await dialog.waitFor();await page.waitForTimeout(250)
 await dialog.getByRole('button',{name:'联合搜索',exact:true}).click();await dialog.locator('.source-choice').first().waitFor()
 await pull(dialog.locator('.source-choice').first(),'作品候选')
 await dialog.locator('.source-choice').first().click();await dialog.locator('.episode-choice').first().waitFor();await pull(dialog.locator('.episode-choice').first(),'剧集候选')
 await pull(dialog.getByRole('button',{name:'整个季度',exact:true}),'范围选项')
 check(Math.max(...report.gestures.map(g=>g.y))-Math.min(...report.gestures.map(g=>g.y))<.3,'各类按钮垂直手势位移一致')
 await dialog.getByRole('button',{name:'预览已选影片并下载',exact:true}).click();const batch=page.getByRole('dialog',{name:'预览已选影片并下载',exact:true});await batch.waitFor();check(await batch.locator('tbody tr').count()===12,'动画调整后整季预览仍展示完整12集')
 await batch.getByRole('button',{name:'关闭此页面'}).click();await batch.waitFor({state:'detached'});check(await dialog.count()===1,'关闭子弹窗保留上级来源弹窗')
 await dialog.getByRole('button',{name:'关闭此页面'}).click();await dialog.waitFor({state:'detached'})
 await page.getByRole('button',{name:'管理接口',exact:true}).click();const sources=page.getByRole('dialog',{name:'管理接口',exact:true});await sources.waitFor();await page.waitForTimeout(250)
 const resize=await sources.evaluate(el=>({resize:getComputedStyle(el).resize,resizer:getComputedStyle(el,'::-webkit-resizer').backgroundImage}));check(resize.resize==='both'&&resize.resizer==='none','隐藏可见缩放箭头并保留双向缩放')
 const before=await sources.boundingBox();await page.mouse.move(before.x+before.width-3,before.y+before.height-3);await page.mouse.down();await page.mouse.move(before.x+before.width-100,before.y+before.height-65,{steps:12});await page.mouse.up();await page.waitForTimeout(200);const after=await sources.boundingBox();report.resize={before,after};check(after.width<before.width-50,'通过右下角不可见命中区实际缩放弹窗成功')
 await page.screenshot({path:path.join(out,`dialog-resize-${dpi}.png`)})
 await page.keyboard.press('Escape');await sources.waitFor({state:'detached'})
 await page.locator('.sidebar').getByRole('button',{name:'启动与偏好',exact:true}).click();await page.getByRole('combobox',{name:'界面缩放'}).click();await page.getByRole('option',{name:'120%',exact:true}).click();await page.getByRole('button',{name:'保存偏好',exact:true}).click();await page.waitForTimeout(300)
 await page.locator('.sidebar').getByRole('button',{name:'下载任务',exact:true}).click();await pull(page.getByRole('button',{name:'重试失败项',exact:true}),'120缩放拖动',true)
 await page.getByRole('combobox',{name:'筛选任务'}).click();await page.waitForTimeout(250);const menu=await page.locator('.select-menu').boundingBox();const view=await page.evaluate(()=>({w:innerWidth,h:innerHeight}));check(menu.x>=0&&menu.y>=0&&menu.x+menu.width<=view.w+1&&menu.y+menu.height<=view.h+1,'120% 下拉框留出动画空间且不超出窗口')
 check(report.errors.length===0,'相关页面无脚本异常');report.passed=true
} catch(e){report.errors.push(e.stack);throw e} finally {await fs.writeFile(path.join(out,`result-${dpi}.json`),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,dpi,resize:report.resize},null,2))
