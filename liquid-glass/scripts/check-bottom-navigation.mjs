import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/bottom-navigation'),report={passed:false,cases:[],errors:[],timings:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const check=(v,n)=>{assert(v,n);report.cases.push(n)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(14000);page.on('pageerror',e=>report.errors.push(e.message));const cdp=await page.context().newCDPSession(page)
 await page.evaluate(()=>{window.__navEvents=[];for(const type of ['blur','focus'])window.addEventListener(type,()=>window.__navEvents.push({type,time:performance.now(),pose:{...document.querySelector('.dock-hit-surface')?.dataset}}))})
 await page.evaluate(()=>{window.__navFrames=[];const raf=window.requestAnimationFrame.bind(window);window.requestAnimationFrame=callback=>raf(time=>{const start=performance.now();callback(time);window.__navFrames.push({time,work:performance.now()-start,lift:document.querySelector('.dock-hit-surface')?.dataset.lift})})})
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1320,880);w.show();w.focus()})
 const rail=page.locator('.dock-hit-surface'),heading=()=>page.locator('.page-heading h1').textContent(),settle=()=>page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled'&&!document.documentElement.getAnimations({subtree:true}).some(a=>a.effect?.pseudoElement?.includes('view-transition')))
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await settle()
 const capture=async name=>{await page.waitForTimeout(100);const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'))}
 check(await page.getByRole('tab').count()===8&&await page.locator('.sidebar').count()===0,'all eight existing entries live in the bottom shared rail')
 check(await page.locator('.page-scene').evaluate(el=>Number(getComputedStyle(el).opacity)<1),'scene keeps a stable compositor backdrop root')
 await capture('01-overview')
 check(await page.locator('.dock-lens').evaluate(el=>Number(getComputedStyle(el).opacity)<=.01)&&await page.locator('.dock-body>.dock-labels').evaluate(el=>getComputedStyle(el).clipPath==='none'),'idle reference handoff displays original native labels without replay seams')
 const tab=label=>page.getByRole('tab',{name:label,exact:true})
 const go=async label=>{await tab(label).click();await page.getByRole('heading',{name:label,exact:true}).waitFor();await settle();await page.waitForTimeout(320)}
 // Held contact navigates without waiting for the physical spring to land.
 const r=await tab('影片与弹幕').boundingBox();await page.mouse.move(r.x+r.width/2,r.y+r.height/2);await page.mouse.down();await page.waitForTimeout(380)
 check(await heading()==='影片与弹幕','touch contact mounts the destination before release')
 check(Number(await rail.getAttribute('data-lift'))>.95,'held lens rises with reference contact/body response')
 await capture('02-held-lens');await page.mouse.up();const released=Date.now()
 check(await heading()==='影片与弹幕','release retains the immediately selected destination')
 await page.getByRole('heading',{name:'影片与弹幕',exact:true}).waitFor();report.timings.push({scenario:'tap-landing-before-page',ms:Date.now()-released})
 await settle();check(Number(await rail.getAttribute('data-lift'))<=.08,'lens settles independently of the already mounted page')
 check(Date.now()-released<550,'landing starts page transition without the previous extra half-second wait')
 await settle();await page.waitForTimeout(350);await page.getByRole('button',{name:'列表视图',exact:true}).click();await capture('03-library')
 check(await page.locator('.dock-lens').evaluate(el=>Number(getComputedStyle(el).opacity)<=.01)&&await page.locator('.dock-resting-selection').evaluate(el=>Math.abs(Number(getComputedStyle(el).opacity)-.105)<.0001),'reference lens returns to the same resting fill after all deformation axes settle')
 // Source matching still opens above the dock and stays keyboard accessible.
 await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();await page.getByRole('dialog',{name:'选择弹幕来源',exact:true}).waitFor();await page.waitForTimeout(650);await capture('04-dialog');await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'})
 check(await heading()==='影片与弹幕','modal close retains scene and dock selection')
 // Native screencast of the actual drag and post-landing page transition.
 const frames=[];let frameNo=0;await fs.mkdir(path.join(out,'recording'),{recursive:true})
 const writes=[];cdp.on('Page.screencastFrame',event=>{const name=String(frameNo++).padStart(4,'0')+'.jpg';frames.push({file:name,time:event.metadata.timestamp});writes.push(fs.writeFile(path.join(out,'recording',name),Buffer.from(event.data,'base64')));cdp.send('Page.screencastFrameAck',{sessionId:event.sessionId}).catch(()=>{})})
 await cdp.send('Page.startScreencast',{format:'jpeg',quality:92,maxWidth:1400,maxHeight:1000,everyNthFrame:1})
 const a=await tab('影片与弹幕').boundingBox(),b=await tab('定时任务').boundingBox()
 await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();await page.waitForTimeout(260)
 await page.mouse.move(b.x+b.width/2,b.y+b.height/2-8,{steps:30});await page.waitForTimeout(220)
 check(await heading()==='定时任务','dragging changes pages before pointer release')
 await capture('05-drag-lens');await page.mouse.up();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor();await page.waitForTimeout(450)
 await cdp.send('Page.stopScreencast');await Promise.all(writes);await fs.writeFile(path.join(out,'recording/frames.json'),JSON.stringify(frames,null,2));check(frames.length>12,'native recording covers continuous drag, settling and scene transition')
 // Reverse direction and cancel outside the vertical band.
 const from=await tab('定时任务').boundingBox(),far=await tab('运行日志').boundingBox(),back=await tab('设备与媒体库').boundingBox()
 await page.mouse.move(from.x+from.width/2,from.y+from.height/2);await page.mouse.down();await page.mouse.move(far.x+far.width/2,far.y+far.height/2,{steps:14});await page.mouse.move(back.x+back.width/2,back.y+back.height/2,{steps:18});await page.mouse.up();await page.getByRole('heading',{name:'设备与媒体库',exact:true}).waitFor();await settle();check(await tab('设备与媒体库').getAttribute('aria-selected')==='true','rapid reversal commits only the last selected destination')
 const current=await tab('设备与媒体库').boundingBox();await page.mouse.move(current.x+current.width/2,current.y+current.height/2);await page.mouse.down();await page.mouse.move(current.x+current.width/2,current.y-80,{steps:8});await page.mouse.up();await settle();check(await heading()==='设备与媒体库','vertical drag cancellation does not navigate')
 await tab('设备与媒体库').click();await page.keyboard.press('ArrowRight');await page.getByRole('heading',{name:'启动与偏好',exact:true}).waitFor();await settle();check(await tab('启动与偏好').getAttribute('aria-selected')==='true','arrow-key navigation follows selected destination after a drag')
 // Edge overpull must spring back without clipping or selecting an absent item.
 const first=await tab('服务总览').boundingBox();await page.mouse.move(first.x+first.width/2,first.y+first.height/2);await page.mouse.down();await page.mouse.move(first.x-100,first.y+first.height/2-4,{steps:20});await page.waitForTimeout(180);await capture('06-overpull');await page.mouse.up();await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await settle()
 await tab('服务总览').focus();await page.keyboard.press('End');await page.getByRole('heading',{name:'运行日志',exact:true}).waitFor();await settle();check(await tab('运行日志').evaluate(el=>el===document.activeElement),'End selects and focuses the last destination')
 await page.keyboard.press('Home');await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await settle()
 for(const label of ['下载任务','设备与媒体库','启动与偏好','缓存管理','运行日志']){await go(label);check(await tab(label).getAttribute('aria-selected')==='true',label+' destination remains accessible')}
 await go('影片与弹幕')
 // Both scale choices, minimum window, and all labels retain full layout.
 for(const [width,height,scale] of [[1320,880,100],[940,640,100],[940,640,120]]){
  await app.evaluate(({BrowserWindow},size)=>BrowserWindow.getAllWindows()[0].setSize(...size),[width,height])
  await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.waitForTimeout(600);await settle()
  const fit=await page.evaluate(()=>{const root=document.querySelector('.dock-hit-surface'),r=root.getBoundingClientRect();return{rect:r.toJSON(),width:innerWidth,height:innerHeight,overflow:document.documentElement.scrollWidth>innerWidth,labels:[...document.querySelectorAll('.dock-body > .dock-labels .dock-label>span')].map(el=>({text:el.textContent,fit:el.scrollWidth<=el.clientWidth,rect:el.getBoundingClientRect().toJSON()}))}})
  check(!fit.overflow&&fit.rect.left>0&&fit.rect.right<fit.width&&fit.rect.bottom<fit.height,`${width}/${scale}: dock and scene fit viewport without a horizontal page scrollbar`)
  check(fit.labels.length===8&&fit.labels.every(x=>x.fit),`${width}/${scale}: all eight labels remain complete`)
  await capture(`07-layout-${width}-${scale}`)
  if(scale===120){const table=page.locator('.media-table');await table.scrollIntoViewIfNeeded();const visible=await table.evaluate(el=>{const r=el.getBoundingClientRect(),p=document.querySelector('.page-content').getBoundingClientRect();return Math.min(r.bottom,p.bottom)-Math.max(r.top,p.top)});check(visible>160,'minimum window at 120% keeps media table reachable by scrolling');await capture('08-small-scrolled-table')}
 }
 await page.emulateMedia({reducedMotion:'reduce'});await tab('缓存管理').click();await page.getByRole('heading',{name:'缓存管理',exact:true}).waitFor();check(await rail.getAttribute('data-motion')==='settled','reduced motion commits without a running frame loop')
 check(report.errors.length===0,'new navigation and scene transitions have no renderer errors');report.passed=true
}catch(error){report.errors.push(error.stack);try{const p=app.windows()[0];report.failure=await p.evaluate(()=>({focus:document.hasFocus(),heading:document.querySelector('.page-heading h1')?.textContent,pose:{...document.querySelector('.dock-hit-surface')?.dataset},events:window.__navEvents,frames:window.__navFrames,cachedBackground:Boolean(document.querySelector('.dock-cached-background img')?.src)}));const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,'failure.png'),Buffer.from(png,'base64'))}catch{}throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,timings:report.timings}))
