import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/desktop-performance-'+Date.now())
await fs.mkdir(out,{recursive:true})
const exe=process.env.DANMU_TEST_DESKTOP||path.resolve('tools/electron/electron.exe'),installed=!!process.env.DANMU_TEST_DESKTOP
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
if(process.argv.includes('--lifecycle')||process.argv.includes('--after-playback')){env.DANMU_QA_SCRIPT=path.join(out,'lifecycle.cjs');await fs.writeFile(env.DANMU_QA_SCRIPT,'module.exports=api=>global.__desktopLifecycle=api')}
const app=await _electron.launch({executablePath:exe,args:[...(installed?[]:['.']),'--qa'],env,timeout:40000}),report={phases:[],errors:[]}
try{
 let page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 if(process.env.DANMU_PERF_CSS)await page.addStyleTag({content:process.env.DANMU_PERF_CSS})
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.maximize();w.show();w.focus()})
 report.gpu=await app.evaluate(async({app})=>({features:app.getGPUFeatureStatus(),info:await app.getGPUInfo('complete')}))
 report.display=await app.evaluate(({screen,BrowserWindow})=>({display:screen.getDisplayMatching(BrowserWindow.getAllWindows()[0].getBounds()),bounds:BrowserWindow.getAllWindows()[0].getBounds()}))
 if(process.argv.includes('--after-playback')){
  await app.evaluate(async(_,video)=>{const p=global.__desktopLifecycle.getPlayer();await p.configure({resume:false,volume:0,enabled:false});await p.play([{key:'test',path:video,title:'关闭播放后的界面检查'}],'test')},path.resolve('artifacts/local-player/media/4K-HEVC10.mkv'))
  await sleep(1200);await app.evaluate(async()=>{await global.__desktopLifecycle.getPlayer().stop();const w=global.__desktopLifecycle.getWindow();w.show();w.focus()});report.afterPlayback=true
 }
 const cdp=await page.context().newCDPSession(page)
 await cdp.send('Tracing.start',{categories:'devtools.timeline,disabled-by-default-devtools.timeline.frame,cc,viz,gpu,benchmark',transferMode:'ReturnAsStream'})
 await page.evaluate(()=>{window.__perf={active:false,generation:0,times:[],long:[]};new PerformanceObserver(l=>{if(window.__perf.active)window.__perf.long.push(...l.getEntries().map(x=>({start:x.startTime,duration:x.duration,name:x.name})))}).observe({type:'longtask',buffered:false})})
 async function phase(name,action){
  await page.evaluate(name=>{console.timeStamp('qa-start:'+name);const p=window.__perf,generation=++p.generation;p.active=true;p.times=[];p.long=[];const frame=t=>{if(p.active&&generation===p.generation){if(t!==p.times.at(-1))p.times.push(t);requestAnimationFrame(frame)}};requestAnimationFrame(frame)},name)
  const began=Date.now();await action();await page.waitForTimeout(350)
  const data=await page.evaluate(name=>{const p=window.__perf;p.active=false;console.timeStamp('qa-end:'+name);const dt=p.times.slice(1).map((t,i)=>t-p.times[i]),a=[...dt].sort((a,b)=>a-b);return{frames:dt.length,p50:a[Math.floor(a.length*.5)],p95:a[Math.floor(a.length*.95)],p99:a[Math.floor(a.length*.99)],max:Math.max(...dt),over16:dt.filter(t=>t>16.8).length,long:p.long,dpr:devicePixelRatio,viewport:[innerWidth,innerHeight]}},name)
  report.phases.push({name,ms:Date.now()-began,...data});console.log(JSON.stringify(report.phases.at(-1)))
 }
 await phase('cold buttons',async()=>{const box=await page.locator('.page-content .glass-button').first().boundingBox();for(let i=0;i<12;i++){await page.mouse.move(box.x+box.width/2,box.y+box.height/2,{steps:3});await page.waitForTimeout(40);await page.mouse.move(box.x+box.width+15,box.y+box.height+15,{steps:3});await page.waitForTimeout(40)}})
 await phase('dock drag',async()=>{const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'运行日志',exact:true}).boundingBox();await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();for(let i=0;i<64;i++){const f=i<32?i/31:(63-i)/31;await page.mouse.move(a.x+a.width/2+(b.x+b.width/2-a.x-a.width/2)*f,a.y+a.height/2-4);await page.waitForTimeout(8)}await page.mouse.up()})
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor();await page.waitForTimeout(300)
 await phase('cover hover and scroll',async()=>{const card=page.locator('.poster-card').first(),r=await card.boundingBox();for(let i=0;i<12;i++){await page.mouse.move(r.x+60,r.y+80,{steps:3});await page.waitForTimeout(40);await page.mouse.move(r.x+r.width+10,r.y+80,{steps:3});await page.mouse.wheel(0,i%2?-90:90);await page.waitForTimeout(40)}})
 await page.getByRole('button',{name:'本地播放设置',exact:true}).click();await page.getByRole('dialog').waitFor();await page.waitForTimeout(1000)
 await phase('settings drag',async()=>{const slider=page.getByRole('dialog').locator('input[type=range]').first(),r=await slider.boundingBox();await page.mouse.move(r.x+r.width*.3,r.y+r.height/2);await page.mouse.down();for(let i=0;i<70;i++){await page.mouse.move(r.x+r.width*(.3+.5*(i<35?i:69-i)/34),r.y+r.height/2);await page.waitForTimeout(12)}await page.mouse.up()})
 await page.screenshot({path:path.join(out,'settings.png')});await page.keyboard.press('Escape')
 const completed=new Promise(resolve=>cdp.once('Tracing.tracingComplete',resolve));await cdp.send('Tracing.end');const complete=await completed;let trace=''
 for(;;){const chunk=await cdp.send('IO.read',{handle:complete.stream});trace+=chunk.base64Encoded?Buffer.from(chunk.data,'base64').toString():chunk.data;if(chunk.eof)break}await cdp.send('IO.close',{handle:complete.stream});await fs.writeFile(path.join(out,'trace.json'),trace)
 const events=JSON.parse(trace).traceEvents,counts={};for(const e of events)if(/DrawFrame|FramePresented|PipelineReporter|SwapBuffers|FrameSequenceTracker/.test(e.name))counts[e.name]=(counts[e.name]||0)+1;report.traceCounts=counts
 for(const phase of report.phases){const start=events.find(e=>e.name==='TimeStamp'&&e.args?.data?.message==='qa-start:'+phase.name)?.ts,end=events.find(e=>e.name==='TimeStamp'&&e.args?.data?.message==='qa-end:'+phase.name)?.ts;const times=events.filter(e=>e.name==='SkiaOutputSurfaceImplOnGpu::SwapBuffers'&&e.ts>=start&&e.ts<=end).map(e=>e.ts).sort((a,b)=>a-b),dt=times.slice(1).map((v,i)=>(v-times[i])/1000).sort((a,b)=>a-b);phase.compositor={frames:times.length,p50:dt[Math.floor(dt.length*.5)],p95:dt[Math.floor(dt.length*.95)],max:Math.max(...dt)}}
 if(process.argv.includes('--lifecycle')){
  const drag=async()=>{const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'定时任务',exact:true}).boundingBox();await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();for(let i=0;i<64;i++){await page.mouse.move(a.x+a.width/2+(b.x+b.width/2-a.x-a.width/2)*i/63,a.y+a.height/2);await page.waitForTimeout(8)}await page.mouse.up()}
  await app.evaluate(()=>global.__desktopLifecycle.getWindow().hide());await sleep(1000);await app.evaluate(()=>{const w=global.__desktopLifecycle.getWindow();w.show();w.focus()});await phase('background restore',drag)
  await sleep(15000);await phase('idle 15 seconds',drag)
  const next=app.waitForEvent('window',{timeout:20000});await app.evaluate(()=>{global.__desktopLifecycle.getWindow().close();global.__desktopLifecycle.show()});page=await next;page.setDefaultTimeout(12000);await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__desktopLifecycle.getWindow();w.maximize();w.show();w.focus()})
  await page.evaluate(()=>window.__perf={active:false,generation:0,times:[],long:[]});await phase('tray renderer recreation',drag)
 }
 report.metrics=await app.evaluate(({app})=>app.getAppMetrics());console.log(JSON.stringify({out,display:report.display,features:report.gpu.features,traceCounts:counts}))
}catch(error){report.error=error.stack;console.error(error);process.exitCode=1}
finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close()}
function sleep(ms){return new Promise(resolve=>setTimeout(resolve,ms))}
