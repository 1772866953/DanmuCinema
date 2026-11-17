import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/dialog-compositing'),dpi=process.argv.includes('--dpi=1.5')?'1.5':'1.25'
await fs.mkdir(out,{recursive:true});const hook=path.join(out,'login-mock.cjs');await fs.writeFile(hook,"module.exports=({app})=>{app.getLoginItemSettings=()=>({openAtLogin:false,launchItems:[]})}")
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+dpi+'-'+Date.now()),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${dpi}`],env})
const report={passed:false,dpi,cases:[],errors:[],frames:{}};let page
const check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 page=await app.firstWindow();page.setDefaultTimeout(14000);page.on('pageerror',error=>report.errors.push(error.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 const nav=async name=>{await page.locator('.sidebar').getByRole('button',{name,exact:true}).click();await page.getByRole('heading',{name,exact:true}).waitFor();await page.mouse.move(2,2);await page.waitForTimeout(350)}
 const capture=async(file,rect)=>{const png=await app.evaluate(async({BrowserWindow},rect)=>{const w=BrowserWindow.getAllWindows()[0],z=w.webContents.getZoomFactor();const clip=rect?{x:Math.round(rect.x*z),y:Math.round(rect.y*z),width:Math.round(rect.width*z),height:Math.round(rect.height*z)}:undefined;return(await w.webContents.capturePage(clip)).toPNG().toString('base64')},rect);await fs.writeFile(path.join(out,file),Buffer.from(png,'base64'))}
 const record=async name=>page.evaluate(name=>{
  window.__paneFrames={name,frames:[]};const start=performance.now()
  const sample=()=>{
   const layer=[...document.querySelectorAll('.modal-layer')].at(-1),pane=layer?.querySelector('.modal-presentation')
   if(pane&&pane.getAttribute('aria-label')===(name==='sources-child'||name==='reduced'?'管理接口':'选择弹幕来源')){const r=pane.getBoundingClientRect(),s=getComputedStyle(pane),m=new DOMMatrixReadOnly(s.transform),l=getComputedStyle(layer),root=layer.getBoundingClientRect();window.__paneFrames.frames.push({at:performance.now()-start,opacity:Number(s.opacity),shade:Number(l.opacity),zoom:Number(s.zoom),scale:m.a,rect:[r.x,r.y,r.width,r.height],offset:[pane.offsetWidth,pane.offsetHeight],center:r.x+r.width/2-root.width/2,matrix:[m.a,m.b,m.c,m.d,m.e,m.f],blur:l.backdropFilter,outline:s.outlineStyle,materials:[...pane.querySelectorAll('.glass-material')].map(el=>({mode:el.dataset.optics,filter:getComputedStyle(el).backdropFilter})),labelTransform:[...pane.querySelectorAll('.glass-content')].map(el=>{const m=new DOMMatrixReadOnly(getComputedStyle(el).transform);return[m.a,m.b,m.c,m.d]})})}
   if(performance.now()-start<750)requestAnimationFrame(sample)
  };requestAnimationFrame(sample)
 },name)
 const frames=async name=>{await page.waitForTimeout(800);const data=await page.evaluate(()=>window.__paneFrames.frames);report.frames[name]=data;return data}
 const verifyEntry=(data,name)=>{
  check(data.length>4&&data.some(f=>f.opacity>.05&&f.opacity<.95)&&data.some(f=>f.scale>.94&&f.scale<.999),name+'：整体缩放与淡入有连续中间帧')
  check(data.every(f=>f.blur==='blur(9px)'),name+'：遮罩从首帧到完成保持与听页相同的9px背景模糊')
  check(data.every(f=>f.materials.every(m=>m.mode==='inherited'&&m.filter==='none')),name+'：内部所有按钮复用表面，无二次滤镜切换')
  check(data.every(f=>f.outline==='none'),name+'：不存在独立固定尺寸的焦点矩形边框')
  check(data.every(f=>Math.abs(f.rect[2]-f.offset[0]*f.scale)<1.6&&Math.abs(f.center)<1.6),name+'：边框和内容保持统一几何且始终水平居中')
  check(data.every(f=>f.zoom===1&&Math.abs(f.matrix[0]-f.matrix[3])<.0001&&f.matrix[1]===0&&f.matrix[2]===0),name+'：使用听页的固定布局与整体transform，不再动态zoom重排')
  check(data.every(f=>f.offset[0]===data[0].offset[0]&&f.offset[1]===data[0].offset[1]),name+'：动画全过程逻辑宽高保持不变')
  check(Math.abs(data.at(-1).scale-1)<.00001&&Math.abs(data.at(-1).matrix[5])<.01,name+'：结束后精确恢复100%与原位置')
 }
 const stress=async(dialog,name)=>{
  await page.mouse.move(2,2);await page.waitForTimeout(700)
  const before=await dialog.boundingBox(),samples=[]
  for(let i=0;i<8;i++){
   if(i===2)await dialog.evaluate(el=>{const pad=document.createElement('div');pad.dataset.stressPad='true';pad.style.height='1200px';el.querySelector('.modal-body').append(pad)})
   if(i===5)await dialog.evaluate(el=>el.querySelector('[data-stress-pad]')?.remove())
   const buttons=dialog.getByRole('button');if(await buttons.count())await buttons.nth(i%await buttons.count()).hover()
   await page.waitForTimeout(45);samples.push(await dialog.boundingBox())
  }
  await dialog.evaluate(el=>el.querySelector('[data-stress-pad]')?.remove());await page.mouse.move(2,2)
  check(samples.every(r=>Math.abs(r.x-before.x)<.15&&Math.abs(r.y-before.y)<.15&&Math.abs(r.width-before.width)<.15&&Math.abs(r.height-before.height)<.15),name+'：反复悬停及滚动条出现/消失不改变外框，不产生尺寸反馈抖动')
 }
 await nav('影片与弹幕')
 const cdp=await page.context().newCDPSession(page),captured=[],pending=[],video=path.join(out,'frames-'+dpi);await fs.mkdir(video,{recursive:true})
 cdp.on('Page.screencastFrame',frame=>{const file=String(captured.length).padStart(3,'0')+'.png';captured.push({file,time:frame.metadata.timestamp*1000});pending.push(fs.writeFile(path.join(video,file),Buffer.from(frame.data,'base64')));cdp.send('Page.screencastFrameAck',{sessionId:frame.sessionId}).catch(()=>{})})
 await cdp.send('Page.startScreencast',{format:'png',maxWidth:1420,maxHeight:960,everyNthFrame:1})
 await page.locator('tbody tr').first().getByRole('button',{name:'星海旅行 第一季',exact:true}).dblclick();await page.waitForFunction(()=>document.querySelectorAll('tbody tr').length===12);await page.locator('thead').getByRole('checkbox').click();await page.waitForTimeout(350)
 await record('match-first');await page.getByRole('button',{name:'匹配已选影片',exact:true}).click();const match=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await match.waitFor();await page.mouse.move(2,2);verifyEntry(await frames('match-first'),'匹配弹窗首次打开')
 await stress(match,'匹配来源');check(await match.locator('.modal-material').count()===0,'弹窗边框不再使用独立缩放层')
 await capture('match-'+dpi+'.png')
 await record('sources-child');await match.getByRole('button',{name:'管理接口',exact:true}).click();const sources=page.getByRole('dialog',{name:'管理接口',exact:true});await sources.waitFor();await page.mouse.move(2,2);verifyEntry(await frames('sources-child'),'接口子弹窗');await stress(sources,'接口管理')
 await page.keyboard.press('Escape');await sources.waitFor({state:'detached'});await page.waitForTimeout(300)
 check(await match.isVisible(),'ESC关闭子弹窗只返回父弹窗')
 const focused=match.getByRole('button',{name:'管理接口',exact:true});const ring=await focused.evaluate(el=>{const m=el.querySelector('.glass-material'),pseudo=getComputedStyle(el,'::after');return{focused:document.activeElement===el,visible:el.matches(':focus-visible'),outline:getComputedStyle(el).outlineStyle,mode:m.dataset.optics,filter:getComputedStyle(m).backdropFilter,ring:{display:pseudo.display,radius:pseudo.borderTopRightRadius,z:pseudo.zIndex,border:pseudo.borderTopWidth},willChange:getComputedStyle(m).willChange}})
 report.focus=ring;check(ring.focused&&ring.visible&&ring.outline==='none'&&ring.ring.display==='block'&&Number.parseFloat(ring.ring.radius)>20&&Number(ring.ring.z)>2,'ESC返回后保留独立圆角键盘焦点环')
 check(ring.filter==='none'&&ring.willChange==='auto','焦点恢复不触发SVG采样或额外合成提升')
 const r=await focused.boundingBox();await capture('escape-focus-'+dpi+'.png',{x:r.x-12,y:r.y-12,width:r.width+24,height:r.height+24})
 await match.getByRole('textbox',{name:'输入动漫或影片名称'}).fill('星海旅行');await match.getByRole('button',{name:'联合搜索',exact:true}).click();await match.getByRole('listbox',{name:'作品搜索结果'}).getByRole('option').first().click();await match.getByRole('listbox',{name:'在线剧集'}).getByRole('option').nth(11).waitFor();await match.getByRole('button',{name:'预览已选影片并下载',exact:true}).click();const batch=page.getByRole('dialog',{name:'预览已选影片并下载',exact:true});await batch.waitFor();await page.waitForTimeout(650);await stress(batch,'批量预览')
 await page.keyboard.press('Escape');await batch.waitFor({state:'detached'});check(await match.isVisible(),'批量预览ESC返回保留父弹窗')
 await record('match-exit');await page.keyboard.press('Escape');await match.waitFor({state:'detached'});const exited=await frames('match-exit')
 check(exited.some(f=>f.opacity>0&&f.opacity<.95&&f.scale<.999&&f.matrix[5]>0),'弹窗统一以.96目标缩小、12px目标下移及200ms淡出退出')
 check(exited.every(f=>f.blur==='blur(9px)'&&f.materials.every(m=>m.filter==='none')),'退场期间背景模糊和按钮材质没有端点重建')
 check(await page.getByRole('heading',{name:'影片与弹幕',exact:true}).isVisible(),'连续退出嵌套弹窗后主界面仍正常显示')
 await record('match-repeat');await page.getByRole('button',{name:'匹配已选影片',exact:true}).click();await match.waitFor();await page.mouse.move(2,2);verifyEntry(await frames('match-repeat'),'匹配弹窗再次打开')
 // Resizing and dragging remain real interactions after the single-surface rewrite.
 await match.evaluate(el=>{el.style.width='940px';el.style.height='700px'});await page.waitForTimeout(100)
 check(await match.evaluate(el=>el.offsetWidth===940&&el.offsetHeight===700),'仍支持调整弹窗宽高')
 const before=await match.boundingBox(),header=await match.locator('.modal-header').boundingBox();await page.mouse.move(header.x+90,header.y+30);await page.mouse.down();await page.mouse.move(header.x+125,header.y+55,{steps:5});await page.mouse.up();const after=await match.boundingBox();check(Math.abs(after.x-before.x-35)<1&&Math.abs(after.y-before.y-25)<1,'仍支持拖拽弹窗且不触发边框与内容分离')
 await page.keyboard.press('Escape');await match.waitFor({state:'detached'});await page.mouse.move(2,2);await page.waitForTimeout(300)
 const trigger=page.getByRole('button',{name:'匹配已选影片',exact:true});const mainRing=await trigger.evaluate(el=>({focused:document.activeElement===el,outline:getComputedStyle(el).outlineStyle,display:getComputedStyle(el,'::after').display,will:getComputedStyle(el.querySelector('.glass-material')).willChange}));check(mainRing.focused&&mainRing.outline==='none'&&mainRing.display==='block'&&mainRing.will==='auto','返回主页面的焦点环也不进入按钮背景采样')
 const mainBox=await trigger.boundingBox();await capture('main-focus-'+dpi+'.png',{x:mainBox.x-12,y:mainBox.y-12,width:mainBox.width+24,height:mainBox.height+24})
 await page.getByRole('button',{name:'反馈与帮助',exact:true}).click();const feedback=page.getByRole('dialog',{name:'反馈与帮助',exact:true});await feedback.waitFor();await stress(feedback,'反馈帮助');await page.keyboard.press('Escape');await feedback.waitFor({state:'detached'})
 await nav('首次设置');await page.getByRole('button',{name:'管理媒体库',exact:true}).click();const types=page.getByRole('dialog',{name:'管理媒体库',exact:true});await types.waitFor();await stress(types,'媒体库管理');await types.getByRole('button',{name:'删除媒体库',exact:true}).click();const confirm=page.getByRole('dialog',{name:'确认操作',exact:true});await confirm.waitFor();await stress(confirm,'确认操作');await page.keyboard.press('Escape');await confirm.waitFor({state:'detached'});await page.keyboard.press('Escape');await types.waitFor({state:'detached'});await nav('影片与弹幕')
 await page.emulateMedia({reducedMotion:'reduce'});await record('reduced');await page.getByRole('button',{name:'管理接口',exact:true}).click();const reduced=page.getByRole('dialog',{name:'管理接口',exact:true});await reduced.waitFor();await page.waitForTimeout(150);check(await reduced.evaluate(el=>Number(getComputedStyle(el).zoom)===1&&new DOMMatrixReadOnly(getComputedStyle(el).transform).a===1&&new DOMMatrixReadOnly(getComputedStyle(el).transform).f===0),'减少动态效果下不执行缩放上浮')
 await page.keyboard.press('Escape');await reduced.waitFor({state:'detached'});await cdp.send('Page.stopScreencast');await Promise.all(pending);await cdp.detach();await fs.writeFile(path.join(video,'frames.json'),JSON.stringify(captured));report.captured=captured.length
 check(captured.length>20,'保存实际动画帧供检查闪烁与焦点方块')
 check(report.errors.length===0,'本轮打开、ESC返回与弹窗动作无渲染异常');report.passed=true
}catch(error){report.errors.push(error.stack);if(page)await page.screenshot({path:path.join(out,'failure-'+dpi+'.png')}).catch(()=>{});throw error}finally{await fs.writeFile(path.join(out,'result-'+dpi+'.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,captured:report.captured,dpi}))
