import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {createRequire} from 'node:module'
import {runtime} from './test-runtime.mjs'
const require=createRequire(import.meta.url)
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/liquid-ui'),profile=path.join(out,'profile-'+Date.now())
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:profile};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env,timeout:30000})
const report={passed:false,cases:[],errors:[]};const check=(value,message)=>{assert(value,message);report.cases.push(message)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(10000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(450)
 await page.screenshot({path:path.join(out,'01-overview.png')})
 check(await page.locator('.library-optics .glass__warp').count()>0,'使用 liquid-glass-react 的真实材质层')
 const nav=page.getByRole('button',{name:'影片与弹幕',exact:true});await nav.hover();await page.waitForTimeout(350)
 const clarity=await nav.evaluate(el=>{const c=getComputedStyle(el.querySelector(':scope>.glass-content')),m=getComputedStyle(el.querySelector(':scope>.glass-material')),a=new DOMMatrix(c.transform),b=new DOMMatrix(m.transform);return{foreground:[a.a,a.b,a.c,a.d],material:[b.a,b.b,b.c,b.d],filter:c.filter,backdrop:c.backdropFilter,willChange:c.willChange,translation:[a.e,a.f],materialTranslation:[b.e,b.f],dpr:devicePixelRatio}})
 check(JSON.stringify(clarity.foreground)==='[1,0,0,1]'&&clarity.filter==='none'&&clarity.backdrop==='none'&&clarity.willChange==='auto','悬停时文字单位缩放、零剪切，无滤镜或强制合成')
 report.clarity=clarity
 check(clarity.translation.every((v,i)=>Math.abs(v-clarity.materialTranslation[i])<=.5/clarity.dpr+.002),'同帧前景与材质共享位移，误差在半物理像素以内')
 await nav.click();await page.locator('tbody tr').first().waitFor();await page.screenshot({path:path.join(out,'02-library.png')})
 const pull=page.getByRole('button',{name:'管理接口',exact:true}),box=await pull.boundingBox();report.gestures=[]
 for(const [dx,dy] of [[32,0],[-32,0],[0,32],[0,-32],[28,28]]){
  await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();await page.mouse.move(box.x+box.width/2+dx,box.y+box.height/2+dy,{steps:5});await page.waitForTimeout(220)
  const sample=await pull.evaluate(el=>{const m=new DOMMatrix(getComputedStyle(el.querySelector(':scope>.glass-material')).transform),c=new DOMMatrix(getComputedStyle(el.querySelector(':scope>.glass-content')).transform);return{matrix:[m.a,m.b,m.c,m.d],foreground:[c.a,c.b,c.c,c.d],x:m.e,y:m.f}})
  check(JSON.stringify(sample.foreground)==='[1,0,0,1]',`拖拽 (${dx},${dy}) 时文字保持原始尺寸`)
  if(dy===0)check(sample.matrix[0]>sample.matrix[3],'横向拖动主要沿横向伸展')
  if(dx===0)check(sample.matrix[3]>sample.matrix[0],'纵向拖动主要沿纵向伸展')
  if(dx&&dy)check(sample.matrix[1]>.01,'斜向拖动包含对称方向形变')
  report.gestures.push({dx,dy,...sample});await page.mouse.up();await page.waitForTimeout(450)
  check(await page.getByRole('dialog').count()===0,'超过 5px 的拖动不误触业务操作')
 }
 await page.locator('tbody tr').first().getByRole('checkbox').click();await page.getByRole('button',{name:'匹配已选影片',exact:true}).waitFor()
 check(await page.locator('.selection-toolbar').count()===1,'文件夹复选可选整季，批量操作栏有过渡')
 await page.getByRole('button',{name:'匹配已选影片',exact:true}).click();await page.getByRole('dialog',{name:'选择弹幕来源'}).waitFor();await page.waitForTimeout(500)
 const match=page.getByRole('dialog',{name:'选择弹幕来源'});await match.getByRole('textbox',{name:'输入动漫或影片名称'}).fill('星海旅行');await match.getByRole('button',{name:'联合搜索',exact:true}).click();await match.getByRole('option').first().click();await page.getByRole('listbox',{name:'在线剧集'}).getByRole('option').nth(11).waitFor()
 check(await page.getByRole('listbox',{name:'在线剧集'}).getByRole('option').count()===12,'选择作品后展示完整 12 集')
 await page.screenshot({path:path.join(out,'03-matching.png')});await match.getByRole('button',{name:'预览已选影片并下载',exact:true}).click();const batch=page.getByRole('dialog',{name:'预览已选影片并下载'});await batch.waitFor();await page.waitForTimeout(450)
 check(await batch.getByRole('checkbox').count()===14,'预览保留 12 集复选、全选和保留已有 XML')
 await batch.locator('tbody input[type=checkbox]').first().click();await page.waitForTimeout(100);await batch.locator('tbody input[type=checkbox]').first().click();await page.waitForTimeout(100);check(await batch.locator('tbody input[type=checkbox]').first().isChecked(),'批量预览复选框可实际切换')
 await page.screenshot({path:path.join(out,'04-batch.png')})
 await batch.getByRole('button',{name:'关闭此页面',exact:true}).click();await batch.waitFor({state:'detached'});check(await match.isVisible(),'关闭子弹窗仅移除子弹窗')
 await match.getByRole('button',{name:'管理接口',exact:true}).click();const sources=page.getByRole('dialog',{name:'管理接口'});await sources.waitFor();await page.waitForTimeout(400);await page.screenshot({path:path.join(out,'05-sources.png')})
 check(await sources.locator('input:not([type=checkbox])').count()===0,'官方 API 界面只有启用复选框，无密钥输入框')
 await page.keyboard.press('Escape');await sources.waitFor({state:'detached'});check(await match.isVisible(),'Escape 只关闭顶层弹窗并恢复父弹窗')
 await match.getByRole('button',{name:'关闭此页面',exact:true}).click();await match.waitFor({state:'detached'});check(await page.getByRole('heading',{name:'影片与弹幕',exact:true}).isVisible(),'连续关闭嵌套弹窗后主界面仍显示')
 await page.getByRole('combobox',{name:'排序方式'}).click();await page.getByRole('listbox',{name:'排序方式'}).waitFor();await page.screenshot({path:path.join(out,'06-dropdown.png')});await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await page.getByRole('listbox',{name:'排序方式'}).waitFor({state:'detached'});check(true,'下拉框支持方向键与 Enter，选后正常收回')
 for(const [key,title] of [['settings','启动与偏好'],['schedule','定时任务'],['cache','缓存管理'],['logs','运行日志']]){await page.getByRole('button',{name:title,exact:true}).click();await page.waitForTimeout(250);await page.screenshot({path:path.join(out,`07-${key}.png`)})}
 await page.getByRole('button',{name:'定时任务',exact:true}).click();check((await page.getByRole('combobox',{name:'到时操作'}).innerText()).includes('休眠'),'到时操作默认休眠')
 await page.getByRole('combobox',{name:'计时方式'}).click();await page.getByRole('option',{name:'每周计划',exact:true}).click();check(await page.locator('.week-days button').count()===7,'周一至周日共用执行时间')
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(1020,720));await page.waitForTimeout(250);await page.getByRole('button',{name:'启动与偏好',exact:true}).click();await page.screenshot({path:path.join(out,'08-compact.png')})
 const overflow=await page.evaluate(()=>({w:document.documentElement.scrollWidth,c:document.documentElement.clientWidth}));check(overflow.w<=overflow.c,'紧凑窗口无页面横向溢出')
 await page.getByRole('combobox',{name:'界面缩放'}).click();await page.getByRole('option',{name:'120%',exact:true}).click();await page.getByRole('button',{name:'保存偏好',exact:true}).click();await page.waitForTimeout(300);await page.getByRole('combobox',{name:'界面缩放'}).click();await page.getByRole('listbox',{name:'界面缩放'}).waitFor();await page.waitForTimeout(200);await page.screenshot({path:path.join(out,'09-scale120.png')});await page.keyboard.press('Escape')
 const viewport=await page.evaluate(()=>({footer:document.querySelector('.app-footer').getBoundingClientRect().bottom,height:innerHeight,body:document.documentElement.scrollWidth,width:innerWidth}));check(viewport.footer<=viewport.height+1&&viewport.body<=viewport.width,'120% 使用 Chromium 原生缩放，页脚可见且无横向溢出')
 await page.getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.getByRole('button',{name:'管理接口',exact:true}).click();await page.waitForTimeout(450)
 const settle=await page.getByRole('dialog',{name:'管理接口'}).evaluate(el=>({transform:getComputedStyle(el).transform,radius:getComputedStyle(el).borderRadius}));check(settle.transform==='none'&&parseFloat(settle.radius)>20,'弹窗结束时无残留缩放，保持统一圆角')
 await page.evaluate(()=>{window.animationSamples=[];const start=performance.now();function frame(){const el=document.querySelector('.modal-presentation');window.animationSamples.push({t:performance.now()-start,opacity:el?Number(getComputedStyle(el).opacity):null,transform:el?getComputedStyle(el).transform:null});if(performance.now()-start<350)requestAnimationFrame(frame)}requestAnimationFrame(frame)})
 await page.getByRole('dialog',{name:'管理接口'}).getByRole('button',{name:'关闭此页面'}).click();await page.waitForTimeout(380);report.exitFrames=await page.evaluate(()=>window.animationSamples);check(report.exitFrames.some(f=>f.opacity>0&&f.opacity<1)&&report.exitFrames.at(-1).opacity===null,'弹窗退出具有中间透明度，结束后移除命中层')
 await page.emulateMedia({reducedMotion:'reduce'});await page.getByRole('button',{name:'管理接口',exact:true}).click();await page.waitForTimeout(100);check(await page.getByRole('dialog',{name:'管理接口'}).evaluate(el=>getComputedStyle(el).transform)==='none','减少动态效果时取消弹窗缩放与位移');await page.keyboard.press('Escape');await page.waitForTimeout(120);await page.emulateMedia({reducedMotion:'no-preference'})
 async function stable(){await page.waitForTimeout(400);let last='';for(let i=0;i<15;i++){const value=JSON.stringify(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].getBounds()));if(value===last)return;last=value;await page.waitForTimeout(100)}throw new Error('窗口尺寸未稳定')}
 await page.getByRole('button',{name:'最大化或还原'}).click();await stable();check(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].isMaximized()),'标题栏按钮执行真实最大化')
 await page.getByRole('button',{name:'最大化或还原'}).click();await stable();check(!await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].isMaximized()),'标题栏按钮执行真实还原')
 await page.getByRole('button',{name:'最小化',exact:true}).click();await page.waitForTimeout(250);check(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].isMinimized()),'最小化保留窗口');await app.evaluate(({BrowserWindow})=>{BrowserWindow.getAllWindows()[0].restore();BrowserWindow.getAllWindows()[0].focus()});await stable()
 const before=await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].getNormalBounds());await page.getByRole('button',{name:'关闭窗口'}).click().catch(error=>{if(!page.isClosed())throw error});await new Promise(resolve=>setTimeout(resolve,200));check(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows().length)===0,'放入托盘实际销毁渲染窗口')
 const nextWindow=app.waitForEvent('window');await app.evaluate(({app})=>app.emit('second-instance'));const restored=await nextWindow;await restored.waitForLoadState('domcontentloaded');await restored.waitForTimeout(400);check(await restored.getByRole('heading',{name:'影片与弹幕',exact:true}).isVisible(),'托盘恢复保留上次页面');const after=await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].getNormalBounds());report.windowGeometry={before,after};check(before.width===after.width&&before.height===after.height,'托盘恢复保留上次窗口尺寸')
 check(report.errors.length===0,'本轮页面与弹窗无 JavaScript 错误')
 report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify(report,null,2))
