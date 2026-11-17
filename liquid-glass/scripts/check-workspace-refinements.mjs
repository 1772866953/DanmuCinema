import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/workspace-refinements'),report={passed:false,cases:[],layouts:[],errors:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const check=(v,n)=>{assert(v,n);report.cases.push(n)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message));await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 const capture=async name=>{await page.waitForTimeout(100);const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'))}
 const tab=name=>page.getByRole('tab',{name,exact:true})
 const go=async name=>{await tab(name).click();await page.getByRole('heading',{name,exact:true}).waitFor();await page.waitForTimeout(700)}
 const measure=async name=>{
  const data=await page.locator('.fixed-page').evaluate(el=>{const r=el.getBoundingClientRect();return{client:el.clientHeight,scroll:el.scrollHeight,rect:r.toJSON(),overflow:[...el.querySelectorAll('button,input,.check')].filter(n=>n.getClientRects().length).map(n=>({text:n.getAttribute('aria-label')||n.textContent,rect:n.getBoundingClientRect().toJSON()})).filter(n=>n.rect.bottom>r.bottom+1||n.rect.top<r.top-1)}})
  report.layouts.push({name,...data});check(data.scroll<=data.client+1&&data.overflow.length===0,name+': full non-list page and every action fit without scrolling')
 }
 for(const [width,height,scale] of (process.argv.includes('--optics-only')?[]:[[1320,880,100],[940,640,100],[940,640,120]])){
  await app.evaluate(({BrowserWindow},wh)=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(...wh);w.show();w.focus()},[width,height])
  await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.waitForTimeout(650)
  for(const [key,name] of [['overview','服务总览'],['connect','连接设备'],['setup','首次设置'],['settings','启动与偏好'],['schedule','定时任务']]){await go(name);await capture(`${width}-${scale}-${key}`);await measure(`${width}/${scale}/${key}`)}
  for(const [value,name] of [['每周计划','weekly'],['指定时间','at'],['倒计时','delay']]){
   await page.getByRole('combobox',{name:'计时方式',exact:true}).click();await page.getByRole('option',{name:value,exact:true}).click();await page.waitForTimeout(300);await capture(`${width}-${scale}-schedule-${name}`);await measure(`${width}/${scale}/schedule-${name}`)
  }
 }
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(1320,880));await page.evaluate(()=>window.cinema.call('preferences',{scale:100}));await page.waitForTimeout(400)
 await go('运行日志');const logTop=await page.locator('.log-controls').boundingBox(),logBody=await page.locator('.log-view').boundingBox();check(logTop.y+logTop.height<=logBody.y+1,'log retention/save/open/clear commands precede the scrolling log')
 await capture('logs-top-controls');await go('缓存管理');check(await page.locator('.list-hint').evaluate(el=>el.getBoundingClientRect().bottom<=el.parentElement.querySelector('.table-shell').getBoundingClientRect().top),'cache XML retention and navigation hint precedes its long table');await capture('cache-top-hint')
 const root=page.locator('.dock-hit-surface'),lens=page.locator('.dock-lens'),glass=page.locator('.dock-lens-glass')
 check(await page.locator('.dock-lens').count()===1&&await page.locator('.dock-resting-selection').count()===1,'one physical pose supplies the reference dynamic lens and resting fill')
 check(await page.evaluate(()=>getComputedStyle(document.documentElement).viewTransitionName==='none'),'scene transitions exclude the document root, leaving the dock live')
 check(await page.locator('.page-viewport').evaluate(el=>getComputedStyle(el,'::before').backdropFilter==='blur(7px)'&&getComputedStyle(el,'::after').backdropFilter==='blur(7px)'),'both viewport boundaries use Gaussian transition strips')
 // Keep native timestamps for optical/flicker review and post-landing timing.
 const cdp=await page.context().newCDPSession(page),frames=[],writes=[];await fs.mkdir(path.join(out,'recording'),{recursive:true});let number=0
 cdp.on('Page.screencastFrame',e=>{const file=String(number++).padStart(4,'0')+'.jpg';frames.push({file,time:e.metadata.timestamp});writes.push(fs.writeFile(path.join(out,'recording',file),Buffer.from(e.data,'base64')));cdp.send('Page.screencastFrameAck',{sessionId:e.sessionId}).catch(()=>{})})
 await cdp.send('Page.startScreencast',{format:'jpeg',quality:94,maxWidth:1600,maxHeight:1100,everyNthFrame:1})
 for(const [name,delta] of [['服务总览',-120],['运行日志',120]]){
  const target=await tab(name).boundingBox();await page.mouse.move(target.x+target.width/2,target.y+target.height/2);await page.mouse.down();await page.mouse.move(target.x+target.width/2+delta,target.y+target.height/2-3,{steps:24});await page.waitForTimeout(250)
  const r=await root.boundingBox(),l=await lens.boundingBox();check(delta<0?l.x<r.x-3:l.x+l.width>r.x+r.width+3,name+': raised lens protrudes beyond the rail instead of compressing')
  check(l.x>=0&&l.x+l.width<=await page.evaluate(()=>innerWidth),name+': end travel has space inside the window')
  await capture(delta<0?'left-protrusion':'right-protrusion');await page.mouse.up();await page.waitForTimeout(1100)
 }
 const target=await tab('影片与弹幕').boundingBox();await page.mouse.move(target.x+target.width/2,target.y+target.height/2);await page.mouse.down();await page.waitForTimeout(350);await page.mouse.up()
 const before=await glass.evaluate(el=>({opacity:getComputedStyle(el).opacity,filter:getComputedStyle(el).backdropFilter,connected:el.isConnected}));await page.getByRole('heading',{name:'影片与弹幕',exact:true}).waitFor();const atLanding=await root.getAttribute('data-lift');await page.waitForTimeout(700)
 const after=await glass.evaluate(el=>({opacity:getComputedStyle(el).opacity,filter:getComputedStyle(el).backdropFilter,connected:el.isConnected}));check(before.opacity==='1'&&after.opacity==='1'&&before.filter==='none'&&after.filter==='none'&&Number(atLanding)<=.08,'glass surface never toggles opacity/backdrop roots at landing')
 await cdp.send('Page.stopScreencast');await Promise.all(writes);await fs.writeFile(path.join(out,'recording/frames.json'),JSON.stringify(frames,null,2));await capture('library-final')
 check(report.errors.length===0,'current optical/layout/transition changes have no renderer errors');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,process.argv.includes('--optics-only')?'result-optics.json':'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length}))
