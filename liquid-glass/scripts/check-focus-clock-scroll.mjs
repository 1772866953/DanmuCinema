import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),sharp=runtime('sharp'),out=path.resolve('artifacts/focus-clock-scroll-'+Date.now()),report={passed:false,checks:[],fonts:[],corners:[],errors:[]}
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_QA_OFFSCREEN:process.env.DANMU_PREVIEW_NATIVE==='1'?'0':'1',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const root=process.env.DANMU_PACKAGED_ROOT,app=await _electron.launch({executablePath:root?path.join(root,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(root?[]:['.']),'--qa'],env,timeout:40000})
const check=(v,s)=>{assert(v,s);report.checks.push(s)},capture=async()=>Buffer.from(await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].capturePage()).toPNG().toString('base64')),'base64')
try{
 const page=await app.firstWindow();page.setDefaultTimeout(15000);page.on('pageerror',e=>report.errors.push(e.message))
 await page.waitForFunction(()=>!document.querySelector('[data-startup-warmup]')&&['player','sources'].every(name=>document.querySelector(`[data-retained-dialog="${name}"][data-parked="true"]`)))
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor();await page.mouse.move(3,3);await page.waitForTimeout(350)
 const settings=page.getByRole('button',{name:'本地播放设置',exact:true}),box=await settings.boundingBox(),before=await capture()
 await page.keyboard.press('Tab');await settings.focus();await page.waitForTimeout(250)
 check(await settings.evaluate(n=>n.matches(':focus-visible')&&getComputedStyle(n).outlineStyle==='none'&&getComputedStyle(n,'::after').display==='none'),'keyboard focus is retained without adding an outer or inner border')
 const focused=await capture(),meta=await sharp(before).metadata(),view=await page.evaluate(()=>({w:innerWidth,h:innerHeight})),scale=meta.width/view.w,rect={left:Math.floor((box.x-7)*scale),top:Math.floor((box.y-7)*scale),width:Math.ceil((box.width+14)*scale),height:Math.ceil((box.height+14)*scale)},raw=await Promise.all([before,focused].map(b=>sharp(b).extract(rect).raw().toBuffer()))
 let delta=0;for(let i=0;i<raw[0].length;i++)delta+=Math.abs(raw[0][i]-raw[1][i]);check(delta/raw[0].length<.15,'the focused button and its surrounding pixels keep their original appearance')
 await fs.writeFile(path.join(out,'keyboard-focused.png'),focused);await page.keyboard.press('Enter');await page.getByRole('dialog',{name:'本地播放与弹幕',exact:true}).waitFor();check(true,'Enter still opens the focused button');await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'});await page.waitForTimeout(300)
 const corner=async(label,rootSelector,scrollSelector)=>{
  const outer=page.locator(rootSelector).last(),scroll=outer.locator(scrollSelector).last()
  await scroll.evaluate(n=>n.scrollTop=n.scrollHeight);await page.waitForTimeout(120)
  const geometry=await outer.evaluate(n=>{const r=n.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height,radius:parseFloat(getComputedStyle(n).borderBottomRightRadius)}}),s=await scroll.evaluate(n=>({height:n.clientHeight,total:n.scrollHeight,gap:getComputedStyle(n,'::-webkit-scrollbar-track:vertical').marginBottom}))
  report.corners.push({label,geometry,scroll:s});check(s.total>s.height,label+': the fixture exercises an actual scrollbar at the end of its range')
  const visible=await capture(),hide=await page.addStyleTag({content:'::-webkit-scrollbar-thumb{background:transparent!important;border-color:transparent!important}'});await page.waitForTimeout(80);const hidden=await capture();await hide.evaluate(n=>n.remove())
  const m=await sharp(visible).metadata(),v=await page.evaluate(()=>({w:innerWidth,h:innerHeight})),sx=m.width/v.w,sy=m.height/v.h,buffers=await Promise.all([visible,hidden].map(b=>sharp(b).ensureAlpha().raw().toBuffer())),r=geometry.radius;let outsideChanges=0
  for(let y=Math.floor((geometry.y+geometry.h-r-3)*sy);y<Math.min(m.height,Math.ceil((geometry.y+geometry.h+3)*sy));y++)for(let x=Math.floor((geometry.x+geometry.w-r-3)*sx);x<Math.min(m.width,Math.ceil((geometry.x+geometry.w+3)*sx));x++){
   const px=(x+.5)/sx,py=(y+.5)/sy,dx=px-(geometry.x+geometry.w-r),dy=py-(geometry.y+geometry.h-r),outside=px>geometry.x+geometry.w||py>geometry.y+geometry.h||dx>0&&dy>0&&Math.hypot(dx,dy)>r+1
   if(outside){const i=(y*m.width+x)*4;if([0,1,2].some(c=>Math.abs(buffers[0][i+c]-buffers[1][i+c])>3))outsideChanges++}
  }
  report.corners.at(-1).outsideChanges=outsideChanges;check(outsideChanges===0,label+': scrollbar paints no pixels beyond the rounded corner');await fs.writeFile(path.join(out,label+'.png'),visible)
 }
 await page.getByRole('button',{name:'列表视图',exact:true}).click();await page.getByRole('button',{name:'星海旅行 第一季',exact:true}).dblclick();await page.waitForFunction(()=>document.querySelector('.media-name-trigger')?.textContent==='BD');await page.locator('.media-name-trigger').first().dblclick();await page.waitForFunction(()=>document.querySelectorAll('.media-table tbody tr').length===12)
 // Bound the existing rounded table to exercise its native scroll parts even
 // when a large QA viewport could otherwise fit all synthetic rows.
 await page.locator('.media-table').evaluate(n=>Object.assign(n.style,{height:'260px',maxHeight:'260px',minHeight:'0',flex:'0 0 260px'}))
 await corner('media-list','.media-table',':scope')
 await settings.click();await page.getByRole('dialog',{name:'本地播放与弹幕',exact:true}).waitFor();await page.waitForTimeout(550)
 if(process.env.DANMU_PREVIEW_NATIVE==='1'){await fs.writeFile(path.join(out,'ready-native-preview.json'),JSON.stringify({out}));let released=false;for(let i=0;i<3000;i++){if(await fs.access(path.join(out,'continue-native-preview')).then(()=>true,()=>false)){released=true;break}await new Promise(r=>setTimeout(r,100))}assert(released,'native visual inspection timed out')}
 await corner('player-dialog','.modal-layer:not([inert]) .modal-presentation[role=dialog]','.modal-body');await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'});await page.waitForTimeout(250)
 for(const [width,height,zoom] of [[1320,880,1],[940,640,1],[1320,880,1.2],[940,640,1.2]]){
  await app.evaluate(({BrowserWindow},v)=>{const w=BrowserWindow.getAllWindows()[0];w.setContentSize(v.width,v.height);w.webContents.setZoomFactor(v.zoom)},{width,height,zoom});await page.getByRole('tab',{name:'定时任务',exact:true}).click()
  const sizes=[]
  for(const name of ['倒计时','指定时间','每周计划']){
   await page.getByRole('combobox',{name:'计时方式',exact:true}).click();await page.getByRole('option',{name,exact:true}).click();await page.waitForTimeout(150)
   const font=await page.locator('.duration-option[data-selected=true]').first().evaluate(n=>getComputedStyle(n).fontSize);sizes.push(font);report.fonts.push({width,height,zoom,name,font})
   await fs.writeFile(path.join(out,`clock-${name}-${width}-${height}-${zoom}.png`),await capture())
  }
  check(new Set(sizes).size===1&&parseFloat(sizes[0])<=14,`${width}×${height}/${zoom}: countdown, specified-time and weekly digits share the smaller size`)
 }
 check(!report.errors.length,'no renderer errors');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}
finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,passed:report.passed,checks:report.checks,error:report.error,corners:report.corners,fonts:report.fonts},null,2))}
