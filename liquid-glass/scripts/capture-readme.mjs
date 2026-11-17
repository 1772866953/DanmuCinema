import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {runtime} from './test-runtime.mjs'

const project=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..')
const out=path.resolve(project,'../docs/screenshots')
const work=path.join(project,'artifacts/readme-capture')
const profile=path.join(work,'profile-'+Date.now())
const {_electron}=runtime('playwright')
await fs.mkdir(work,{recursive:true})
const sharp=runtime('sharp')
const covers=[]
for(const [i,colors]of [['#142e54','#607d95','#a4cece'],['#5d4f4b','#c49875','#ede0b9'],['#244e46','#6d9981','#bfd3a9']].entries()){
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900"><defs><linearGradient id="g" x2="0.8" y2="1"><stop stop-color="${colors[0]}"/><stop offset="1" stop-color="${colors[1]}"/></linearGradient></defs><rect width="600" height="900" fill="url(#g)"/><circle cx="${380-i*70}" cy="${250+i*50}" r="100" fill="${colors[2]}" opacity=".8"/><path d="M-80 660Q100 280 320 650T720 500V900H-80" fill="${colors[0]}" opacity=".65"/><path d="M-80 850Q200 460 430 770T720 650V900H-80" fill="${colors[2]}" opacity=".45"/>${Array.from({length:22},(_,n)=>`<circle cx="${(n*137+31)%600}" cy="${(n*73+45)%500}" r="${n%3+1}" fill="${colors[2]}" opacity=".65"/>`).join('')}</svg>`
 covers.push('data:image/png;base64,'+(await sharp(Buffer.from(svg)).png().toBuffer()).toString('base64'))
}
const fixture=path.join(work,'fixture.cjs')
await fs.writeFile(fixture,`module.exports=()=>{const b=require(${JSON.stringify(path.join(project,'tests/library-view-fixture.cjs'))})();const images=${JSON.stringify(covers)};return{...b,call:(m,p)=>m==='cover'?images[p.key.includes('午后日记')?1:p.key.includes('山间来信')?2:0]:b.call(m,p)}}`)
const env={...process.env,DANMU_QA_OFFSCREEN:'1',DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:fixture}
delete env.ELECTRON_RUN_AS_NODE
await fs.mkdir(out,{recursive:true})
const app=await _electron.launch({executablePath:path.join(project,'tools/electron/electron.exe'),args:[project,'--qa','--force-device-scale-factor=1'],env,timeout:40000})
const errors=[]
try{
 const page=await app.firstWindow();page.on('pageerror',e=>errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setContentSize(1420,960))
 await page.waitForFunction(()=>!document.querySelector('[data-startup-warmup]')&&['player','sources'].every(name=>document.querySelector(`[data-retained-dialog="${name}"][data-parked="true"]`)))
 async function capture(name){
  await page.mouse.move(1,1);await page.waitForTimeout(850)
  const text=await page.locator('body').innerText()
  for(const word of ['lizim','爱情公寓','赛博朋克','百人女友','圣者无双','C:\\视频','C:\\Users'])assert(!text.toLowerCase().includes(word.toLowerCase()),'Personal information: '+word)
  const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].capturePage()).toPNG().toString('base64'))
  await fs.writeFile(path.join(out,name),Buffer.from(png,'base64'))
 }
 await page.getByRole('tab',{name:'服务总览',exact:true}).click()
 await capture('01-overview.png')
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click()
 await page.locator('.poster-card').first().waitFor()
 await page.locator('.poster-grid').dispatchEvent('wheel',{deltaY:300,ctrlKey:true})
 await capture('02-library.png')
 await page.locator('.poster-card').first().getByRole('checkbox').click()
 await page.getByRole('button',{name:'搜索弹幕',exact:true}).click()
 const match=page.getByRole('dialog',{name:'选择弹幕来源',exact:true})
 await match.getByRole('textbox',{name:'输入动漫或影片名称'}).fill('星海旅行')
 await match.getByRole('button',{name:'联合搜索',exact:true}).click()
 await match.getByRole('listbox',{name:'作品搜索结果'}).getByRole('option').first().click()
 await match.getByRole('listbox',{name:'在线剧集'}).getByRole('option').nth(11).waitFor()
 await capture('03-season-matching.png')
 assert.deepEqual(errors,[])
 console.log('Captured three screenshots with fictional data: '+out)
}finally{await app.close()}
