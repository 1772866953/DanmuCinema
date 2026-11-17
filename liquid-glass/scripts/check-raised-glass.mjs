import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),before=process.argv.includes('--before'),out=path.resolve('artifacts/raised-glass',before?'before':'after'),report={passed:false,cases:[],errors:[],captures:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1'],env})
const check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(14000);page.on('pageerror',error=>report.errors.push(error.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(400)
 await page.evaluate(()=>{window.__opticEvents=[];for(const kind of ['pointerdown','pointerup','pointercancel','lostpointercapture','resize'])window.addEventListener(kind,event=>window.__opticEvents.push({kind,target:event.target?.className,at:performance.now()}),true)})
 const rail=page.locator('.dock-hit-surface'),lens=page.locator('.dock-lens'),settle=()=>page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled')
 const capture=async name=>{
  const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'))
  report.captures.push({name,rect:await lens.evaluate(el=>el.getBoundingClientRect().toJSON()),viewport:await page.evaluate(()=>({width:innerWidth,height:innerHeight,dpr:devicePixelRatio}))})
 }
 for(const [width,height,scale]of [[1920,1080,100],[1920,1080,120],[2560,1440,100],[2560,1440,120]]){
  await app.evaluate(({BrowserWindow},size)=>{const w=BrowserWindow.getAllWindows()[0];w.setPosition(0,0);w.setSize(...size);w.focus()},[width,height]);await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.bringToFront();await page.waitForTimeout(700);await settle();await page.mouse.up()
  const name=`${width}-${scale}`;await capture(name+'-rest')
  if(!before)check(await page.locator('.dock-raised-rim').evaluate(el=>Number(getComputedStyle(el).opacity)===0),name+': raised optical rim is absent at rest')
  const start=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox()
  await page.mouse.move(start.x+start.width/2,start.y+start.height/2);await page.mouse.down();await page.mouse.move(start.x+start.width*2.95,start.y+start.height/2-3,{steps:32});await page.waitForTimeout(400)
  report.lastPose=await rail.evaluate(el=>({pose:{...el.dataset},focus:document.hasFocus(),events:window.__opticEvents}));check(await rail.getAttribute('data-held')==='true'&&Number(await rail.getAttribute('data-lift'))>.95,name+': lens is actually raised while captured')
  await capture(name+'-held')
  if(!before){
   const optical=await page.evaluate(()=>({rim:Number(getComputedStyle(document.querySelector('.dock-raised-rim')).opacity),filter:getComputedStyle(document.querySelector('.dock-lens-clip')).filter,backdrop:getComputedStyle(document.querySelector('.dock-lens-clip')).backdropFilter,masks:[...document.querySelectorAll('.dock-prism-edge')].map(el=>getComputedStyle(el).maskImage),scales:[...document.querySelectorAll('.dock-lens-glass feDisplacementMap')].map(el=>Number(el.getAttribute('scale')))}))
   report.lastOptical=optical;check(optical.rim>.95&&optical.masks.every(mask=>mask.startsWith('url(')),name+': lifted rim uses a feathered optical mask')
   check(optical.scales.length===3&&Math.abs(optical.scales[0])>Math.abs(optical.scales[1])&&Math.abs(optical.scales[1])>Math.abs(optical.scales[2]),name+': wavelength dispersion samples the raised scene')
   check(optical.filter==='none'&&optical.backdrop==='none',name+': centre labels stay native and unfiltered')
  }
  // Return to the original destination so every test starts with the same scene.
  await page.mouse.move(start.x+start.width/2,start.y+start.height/2,{steps:24});await page.mouse.up();await settle();await page.waitForTimeout(300)
  if(!before)check(await lens.evaluate(el=>Number(getComputedStyle(el).opacity)<=.01),name+': dynamic lens hands back to original labels after all springs settle')
 }
 // Controlled source-colour check in the real component, not a decorative rim.
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(1320,880));await page.evaluate(()=>window.cinema.call('preferences',{scale:100}));await page.waitForTimeout(400)
 const first=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox();await page.mouse.move(first.x+first.width/2,first.y+first.height/2);await page.mouse.down();await page.mouse.move(first.x+first.width*2.6,first.y+first.height/2,{steps:25});await page.waitForTimeout(400)
 for(const [name,color]of [['gray','#666666'],['warm','#884d2d'],['cool','#245573']]){
  await page.evaluate(color=>{let style=document.getElementById('test-source');if(!style){style=document.createElement('style');style.id='test-source';document.head.append(style)}style.textContent=`.dock-optic-environment{background:${color}!important}.dock-lens-material{background:#444!important}.dock-labels{visibility:hidden}`},color)
  await page.waitForTimeout(120);await capture('source-'+name)
 }
 await page.evaluate(()=>{document.getElementById('test-source').textContent='.dock-optic-environment{background:#666!important}.dock-lens-material{background:#444!important}'})
 await page.waitForTimeout(100);await capture('dispersed-labels');await page.mouse.up();await settle();await page.evaluate(()=>document.getElementById('test-source')?.remove())
 check(report.errors.length===0,'raised optics have no renderer errors');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,captures:report.captures.length}))
