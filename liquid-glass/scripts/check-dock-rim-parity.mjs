import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'

// Compare the candidate with the existing SVG at the same settled, held pose.
// This is a visual equivalence check, separate from the pacing benchmark.
const {_electron}=runtime('playwright'),sharp=runtime('sharp')
const material=process.argv.includes('--material')
const unified=process.argv.includes('--unified')
const out=path.resolve('artifacts/dock-rim-parity-'+Date.now()),profile=path.join(out,'profile')
await fs.mkdir(profile,{recursive:true})
await fs.writeFile(path.join(profile,'window.json'),JSON.stringify({width:1320,height:880,maximized:true}))
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_EXPERIMENTAL_WEBGL_RIM:material||unified?'0':'1',DANMU_EXPERIMENTAL_MATERIAL_CACHE:material?'1':'0',DANMU_EXPERIMENTAL_DOCK_SHADER:unified?'1':'0'}
delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa'],env,timeout:40000})
const report={passed:false,mode:unified?'unified':material?'material':'rim',cases:[],errors:[]}
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message))
 await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 if(await page.locator('.dock-hit-surface').getAttribute(unified?'data-shader-ready':material?'data-material-cache':'data-rim-renderer')!==(material||unified?'true':'webgl'))throw Error('Candidate not prepared')
 const first=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox()
 await page.mouse.move(first.x+first.width/2,first.y+first.height/2);await page.mouse.down()
 for(const travel of [.6,1.5,3.4]){
  await page.mouse.move(first.x+first.width*(.5+travel),first.y+first.height/2-3,{steps:20});await page.waitForTimeout(600)
  const rect=await page.locator('.bottom-navigation').boundingBox(),images={}
  for(const backend of ['svg','webgl']){
   if(unified)await page.locator('.dock-hit-surface').evaluate((e,backend)=>e.dispatchEvent(new CustomEvent('dock-shader-backend',{detail:backend!=='svg'})),backend)
   else if(material)await page.evaluate(backend=>{let e=document.getElementById('material-parity');if(!e){e=document.createElement('style');e.id='material-parity';document.head.append(e)}e.textContent=backend==='svg'?'.dock-source-material{display:none!important}.dock-recorded-ambient,.dock-source-rail{visibility:visible!important}':''},backend)
   else await page.locator('.dock-hit-surface').evaluate((e,backend)=>e.dispatchEvent(new CustomEvent('dock-rim-backend',{detail:backend})),backend)
   await page.waitForTimeout(unified?500:100)
   const png=Buffer.from(await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64')),'base64')
   const size=await sharp(png).metadata(),view=await page.evaluate(()=>({w:innerWidth,h:innerHeight})),sx=size.width/view.w,sy=size.height/view.h
   images[backend]=await sharp(png).extract({left:Math.floor(rect.x*sx),top:Math.floor(rect.y*sy),width:Math.floor(rect.width*sx),height:Math.floor(rect.height*sy)}).removeAlpha().png().toBuffer()
   await fs.writeFile(path.join(out,`${travel}-${backend}.png`),images[backend])
  }
  const a=await sharp(images.svg).raw().toBuffer(),b=await sharp(images.webgl).raw().toBuffer()
  let sum=0,changed=0,max=0
  for(let i=0;i<a.length;i+=3){let d=0;for(let c=0;c<3;c++){const n=Math.abs(a[i+c]-b[i+c]);sum+=n;d=Math.max(d,n)}if(d>8)changed++;max=Math.max(d,max)}
  report.cases.push({travel,meanChannelDifference:sum/a.length,changedPixels:changed,max})
 }
 await page.mouse.up();report.passed=report.errors.length===0&&report.cases.every(c=>c.meanChannelDifference<.5&&c.changedPixels<250)
}catch(e){report.error=e.stack;process.exitCode=1}finally{await app.close();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report}))}
