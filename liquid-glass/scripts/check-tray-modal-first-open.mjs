import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
import traceHelpers from './trace-presentation.cjs'
const {_electron}=runtime('playwright'),sharp=runtime('sharp'),out=path.resolve('artifacts/tray-modal-first-'+Date.now()),report={passed:false,phases:[],errors:[]}
await fs.mkdir(out,{recursive:true});const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,'module.exports=api=>global.__modalFirst=api')
const ttl=Number(process.env.DANMU_TRAY_CACHE_MS||2000),idle=Number(process.env.DANMU_TRAY_IDLE_MS||0),env={...process.env,DANMU_TRAY_CACHE_MS:String(ttl),DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
report.conditions={ttl,idle,electronArgs:process.env.DANMU_ELECTRON_ARGS||''}
const root=process.env.DANMU_PACKAGED_ROOT,app=await _electron.launch({executablePath:root?path.join(root,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(root?[]:['.']),'--qa',...(process.env.DANMU_ELECTRON_ARGS||'').split('|').filter(Boolean)],env,timeout:40000})
try{
 let page=await app.firstWindow()
 const ready=async()=>{page.setDefaultTimeout(15000);page.on('pageerror',e=>report.errors.push(e.message));await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');for(let i=0;i<120;i++){if(await app.evaluate(()=>global.__modalFirst.getWindow().getOpacity()===1))break;await page.waitForTimeout(50)}await app.evaluate(()=>{const w=global.__modalFirst.getWindow();w.show();w.focus()});await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor();await page.mouse.move(3,3)}
 await ready()
 if(process.env.DANMU_INITIAL_FOREGROUND_GATE==='1'){await fs.writeFile(path.join(out,'ready-for-start.json'),JSON.stringify({out}));let released=false;for(let i=0;i<6000;i++){if(await fs.access(path.join(out,'start-tray-cycle')).then(()=>true,()=>false)){released=true;break}await new Promise(resolve=>setTimeout(resolve,100))}assert(released,'initial foreground activation timed out')}
 for(const [button,title]of [['本地播放设置','本地播放与弹幕'],['管理接口','管理接口'],['搜索弹幕','选择弹幕来源']].filter(([name])=>!process.env.DANMU_MODAL_TEST_BUTTONS||process.env.DANMU_MODAL_TEST_BUTTONS.split('|').includes(name))){
  const oldId=await app.evaluate(()=>global.__modalFirst.getWindow().webContents.getOSProcessId()),hiddenAt=Date.now();await app.evaluate(()=>global.__modalFirst.getWindow().close())
  for(let i=0;i<Math.ceil((ttl+8000)/50);i++){if(await app.evaluate(()=>!global.__modalFirst.getWindow()))break;await page.waitForTimeout(50).catch(()=>new Promise(r=>setTimeout(r,50)))}
  assert(await app.evaluate(()=>!global.__modalFirst.getWindow()),'tray cache must expire before testing the first modal')
  if(idle>Date.now()-hiddenAt)await new Promise(resolve=>setTimeout(resolve,idle-(Date.now()-hiddenAt)))
  const next=app.waitForEvent('window');await app.evaluate(()=>global.__modalFirst.show());page=await next;await ready();assert(await app.evaluate(()=>global.__modalFirst.getWindow().webContents.getOSProcessId())!==oldId)
  if(process.env.DANMU_MODAL_MANUAL_GATE==='1'){await fs.writeFile(path.join(out,'ready-for-foreground.json'),JSON.stringify({button,out}));let released=false;for(let i=0;i<6000;i++){if(await fs.access(path.join(out,'continue-foreground')).then(()=>true,()=>false)){released=true;break}await new Promise(resolve=>setTimeout(resolve,100))}assert(released,'restored foreground activation timed out');await fs.unlink(path.join(out,'continue-foreground')).catch(()=>{})}
  const focused=await app.evaluate(()=>global.__modalFirst.getWindow().isFocused());report.foreground??=[];report.foreground.push({button,focused});if(process.env.DANMU_INITIAL_FOREGROUND_GATE==='1')assert(focused,'the restored test window must actually own foreground focus')
  if(process.env.DANMU_PERF_CSS)await page.addStyleTag({content:process.env.DANMU_PERF_CSS})
  const cdp=await page.context().newCDPSession(page);await cdp.send('Tracing.start',{categories:'blink.user_timing,cc,viz,benchmark',transferMode:'ReturnAsStream'})
 const images=[]
  for(const pass of ['first','repeat']){
   const recording=[]
   const record=frame=>{recording.push(frame);void cdp.send('Page.screencastFrameAck',{sessionId:frame.sessionId})}
   if(process.env.DANMU_MODAL_RECORD==='1'){cdp.on('Page.screencastFrame',record);await cdp.send('Page.startScreencast',{format:'png',maxWidth:1320,maxHeight:880,everyNthFrame:1})}
   await page.getByRole('button',{name:button,exact:true}).evaluate(n=>{
    window.__opening={start:performance.now(),frames:[]};performance.mark('first-modal:'+window.__opening.start);n.click()
    const frame=()=>{const record=window.__opening,node=document.querySelector('.modal-layer:not([inert]) .modal-presentation[role=dialog]');if(node){const shade=node.closest('.modal-layer').querySelector('.modal-scrim'),s=getComputedStyle(shade);record.frames.push({time:performance.now(),node:node.dataset.animation,animationStart:Number(node.dataset.animationStart),duration:Number(node.dataset.animationDuration),panelBackground:getComputedStyle(node).backgroundImage,background:s.backgroundColor,blur:getComputedStyle(document.querySelector('.workspace')).filter,opacity:Number(s.opacity),children:node.childElementCount})}if(performance.now()-record.start<650)requestAnimationFrame(frame)};requestAnimationFrame(frame)
   })
   const dialog=page.getByRole('dialog',{name:title,exact:true});await dialog.waitFor();await page.waitForTimeout(700);const data=await page.evaluate(()=>window.__opening),frames=data.frames
   assert(frames.length>10&&frames.at(-1).blur==='blur(9px)'&&new Set(frames.map(f=>f.background)).size===1&&new Set(frames.map(f=>f.panelBackground)).size===1,'dialog source blur reaches its final radius without swapping the panel material')
   assert(frames.every((f,i)=>i===0||f.opacity+.001>=frames[i-1].opacity),'backdrop cannot disappear and reappear')
   const active=frames.find(f=>f.node==='opening'),phase={button,pass,start:data.start,firstFrameMs:frames[0].time-data.start,animationStart:active?.animationStart,duration:active?.duration,frames,firstShadeOpacity:frames[0].opacity}
   assert(active&&phase.animationStart>=data.start&&phase.animationStart-data.start<120,'first modal animation starts promptly after clicking')
   const image=await page.screenshot();await fs.writeFile(path.join(out,title+'-'+pass+'.png'),image);images.push(image);report.phases.push(phase)
   if(process.env.DANMU_MODAL_RECORD==='1'){await cdp.send('Page.stopScreencast');cdp.off('Page.screencastFrame',record);const folder=path.join(out,title+'-'+pass+'-frames');await fs.mkdir(folder);await Promise.all(recording.map((f,i)=>fs.writeFile(path.join(folder,String(i).padStart(3,'0')+'.png'),Buffer.from(f.data,'base64'))));await fs.writeFile(path.join(folder,'metadata.json'),JSON.stringify(recording.map(({data,...f})=>f)))}
   await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});await page.waitForTimeout(250)
  }
  const done=new Promise(resolve=>cdp.once('Tracing.tracingComplete',resolve));await cdp.send('Tracing.end');const {stream}=await done;let trace='';for(;;){const p=await cdp.send('IO.read',{handle:stream});trace+=p.base64Encoded?Buffer.from(p.data,'base64').toString():p.data;if(p.eof)break}await cdp.send('IO.close',{handle:stream});await fs.writeFile(path.join(out,title+'-trace.json'),trace)
  const events=JSON.parse(trace).traceEvents,presented=traceHelpers.presentedFrames(events)
  for(const phase of report.phases.filter(p=>p.button===button)){const marker=traceHelpers.marker(events,'first-modal:'+phase.start);assert(marker,'timing marker missing');const start=marker.ts+(phase.animationStart-phase.start)*1000;phase.presentation=traceHelpers.presentationSummary(presented,{pid:marker.pid,start,end:start+phase.duration*1000});const swaps=events.filter(e=>e.name==='SkiaOutputSurfaceImplOnGpu::SwapBuffers'&&e.ts>=start&&e.ts<start+phase.duration*1000).map(e=>e.ts).sort((a,b)=>a-b),dt=swaps.slice(1).map((t,i)=>(t-swaps[i])/1000).sort((a,b)=>a-b);phase.submission={firstMs:(swaps[0]-marker.ts)/1000,frames:swaps.length,median:dt[dt.length>>1],max:Math.max(...dt)}}
  const buffers=await Promise.all(images.map(i=>sharp(i).raw().toBuffer()));let sum=0,changes=0;for(let i=0;i<buffers[0].length;i++){const d=Math.abs(buffers[0][i]-buffers[1][i]);sum+=d;if(d>12)changes++}report.phases.at(-1).settledPixelComparison={meanDelta:sum/buffers[0].length,changedFraction:changes/buffers[0].length};assert(sum/buffers[0].length<1,'first and repeated dialog backgrounds must settle to the same image')
  await cdp.detach()
 }
 assert(!report.errors.length);report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}
finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,passed:report.passed,error:report.error,phases:report.phases.map(({frames,...p})=>p)},null,2))}
