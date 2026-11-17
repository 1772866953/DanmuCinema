import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),sharp=runtime('sharp'),out=path.resolve('artifacts/dock-optics-'+Date.now())
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:process.env.DANMU_PACKAGED_ROOT?path.join(process.env.DANMU_PACKAGED_ROOT,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(process.env.DANMU_PACKAGED_ROOT?[]:['.']),'--qa'],env})
const report={passed:false,cases:[]}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(15000);await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.maximize();w.show();w.focus()})
 for(const zoom of [1,1.2]){
  console.log('Checking dock optics at '+zoom)
  await app.evaluate(({BrowserWindow},z)=>BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(z),zoom);await page.waitForTimeout(500)
  const a=await page.getByRole('tab',{name:'服务总览',exact:true}).boundingBox(),b=await page.getByRole('tab',{name:'影片与弹幕',exact:true}).boundingBox();await page.mouse.move(a.x+a.width/2,a.y+a.height/2);await page.mouse.down();await page.mouse.move((a.x+a.width/2+b.x+b.width/2)/2,a.y+a.height/2-8,{steps:15});await page.waitForTimeout(1000)
  const target=page.locator('.dock-prism-edge'),rect=await page.locator('.bottom-navigation').boundingBox(),s=await target.evaluate(el=>{const mask=document.querySelector(`[id="${el.style.maskImage.match(/#([^"\)]+)/)[1]}"]`);return{maskWidth:Number(mask.getAttribute('width')),maskHeight:Number(mask.getAttribute('height')),width:el.clientWidth,height:el.clientHeight,scales:Array.from(el.parentElement.querySelectorAll('feDisplacementMap')).map(n=>Number(n.getAttribute('scale')))}})
  const shot=async()=>Buffer.from(await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64')),'base64');const normal=await shot();await fs.writeFile(path.join(out,`rim-${zoom}.png`),normal);await target.evaluate(el=>el.style.visibility='hidden');await page.waitForTimeout(80);const hidden=await shot();await target.evaluate(el=>el.style.visibility='');const p=await sharp(normal).removeAlpha().raw().toBuffer({resolveWithObject:true}),q=await sharp(hidden).removeAlpha().raw().toBuffer();let changed=0,max=0;for(let i=0;i<q.length;i+=3){const d=Math.max(...[0,1,2].map(j=>Math.abs(p.data[i+j]-q[i+j])));if(d>4)changed++;max=Math.max(max,d)}
  report.cases.push({zoom,...s,changed,max});assert(s.maskWidth>50&&s.maskHeight>30,'mask has explicit nonzero viewport');assert(changed>200&&max>20,'raised rim contributes refracted scene pixels');assert(s.scales.length===3&&s.scales.every(v=>Math.abs(v)>5),'all RGB optical passes remain active');await page.mouse.up();await page.waitForTimeout(600)
 }
 report.passed=true
}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({out,...report},null,2))
