import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/background-features-'+Date.now());await fs.mkdir(out,{recursive:true})
const fixture=path.join(out,'fixture.cjs'),hook=path.join(out,'hook.cjs')
await fs.writeFile(fixture,`module.exports=()=>{const f=require(${JSON.stringify(path.resolve('tests/library-view-fixture.cjs'))})();global.__logs=Array.from({length:1200},(_,i)=>'日志记录 '+i);return {...f,call:async(m,p)=>m==='logs'?global.__logs.join('\\n'):f.call(m,p)}}`)
await fs.writeFile(hook,`module.exports=api=>{global.__background=api;global.__plays=[];const{ipcMain}=require('electron'),original=ipcMain._invokeHandlers.get('player:action');ipcMain.removeHandler('player:action');ipcMain.handle('player:action',(event,action,value)=>{if(action==='open')global.__plays.push(value);return original(event,action,value)})}`)
const env={...process.env,DANMU_QA_OFFSCREEN:'1',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:fixture,DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1'],env,timeout:40000}),report={passed:false,performanceVerified:false,mode:'offscreen; no desktop activation',checks:[],memory:[],restores:[]}
const check=(v,s)=>{assert(v,s);report.checks.push(s)},sleep=ms=>new Promise(r=>setTimeout(r,ms))
let page=await app.firstWindow()
const ready=()=>page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true',null,{timeout:20000})
const tab=id=>page.evaluate(id=>document.getElementById('workspace-tab-'+id).click(),id)
const memory=()=>app.evaluate(({app})=>app.getAppMetrics().map(p=>({pid:p.pid,type:p.type,workingSet:p.memory?.workingSetSize,privateBytes:p.memory?.privateBytes})))
try{
 await ready();check(await app.evaluate(()=>!global.__background.getWindow().isVisible()),'QA window stays hidden throughout startup')
 await tab('library');await page.waitForSelector('.poster-card')
 await page.getByRole('button',{name:'打开 星海旅行 第一季',exact:true}).evaluate(node=>node.click())
 await page.getByRole('button',{name:'打开 BD',exact:true}).waitFor();await page.getByRole('button',{name:'打开 BD',exact:true}).evaluate(node=>node.click())
 await page.getByRole('button',{name:'选择 第 1 集 · 星海旅途',exact:true}).waitFor()
 const title=await page.locator('.poster-card').first().getAttribute('title');check(title.includes('S01E01.mkv')&&title.includes('第 1 集')&&title.includes('D:\\示例媒体'),'card tooltip contains the full episode filename, name and path')
 check(await page.locator('.media-gallery').getAttribute('title')===null,'blank gallery keeps no hover tooltip')
 const selected=await page.locator('.poster-image-button').first().evaluate(async node=>{const t=performance.now();node.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1,button:0}));await new Promise(requestAnimationFrame);return{checked:node.closest('article').querySelector('input').checked,ms:performance.now()-t}})
 report.selection=selected;check(selected.checked,'single mouse click selects on the first render frame without waiting for the double-click interval')
 await page.locator('.poster-image-button').first().evaluate(node=>node.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1,button:0})));await sleep(60)
 await page.locator('.poster-image-button').first().evaluate(node=>{node.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1,button:0}));node.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:2,button:0}));node.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,detail:2,button:0}))});await sleep(180)
 check(await app.evaluate(()=>global.__plays.length)===1,'double click still requests playback exactly once')
 check(!await page.locator('.poster-card input').first().isChecked(),'double click restores the original selection rather than toggling twice')
 check(await page.getByRole('dialog').count()===0,'double click opens no danmu source dialog')
 await tab('logs');await page.waitForFunction(()=>document.querySelector('.log-view')?.textContent.includes('日志记录 1199'))
 const tail=()=>page.locator('.log-view').evaluate(n=>({top:n.scrollTop,gap:n.scrollHeight-n.clientHeight-n.scrollTop}))
 await sleep(80);check((await tail()).gap<3&&(await tail()).top>100,'logs start at the newest line')
 await app.evaluate(()=>global.__logs.push('追加最新日志'));await sleep(3150);check((await tail()).gap<3,'new logs follow the tail while the user is at the bottom')
 await page.locator('.log-view').evaluate(n=>{n.scrollTop=0;n.dispatchEvent(new Event('scroll'))});await sleep(80);await app.evaluate(()=>global.__logs.push('继续追加'));await sleep(3150);check((await tail()).top<3,'reading older logs is not interrupted by automatic updates')
 await tab('library');await tab('logs');await sleep(180);check((await tail()).gap<3,'re-entering logs again shows the latest line')
 for(let i=0;i<2;i++){
  const before=await memory(),old=await app.evaluate(()=>global.__background.getWindow().webContents.getOSProcessId());await app.evaluate(()=>global.__background.getWindow().close())
  for(let j=0;j<80;j++){if(await app.evaluate(()=>global.__background.getWindow()===null))break;await sleep(25)}
  check(await app.evaluate(()=>global.__background.getWindow()===null),`tray ${i}: window and DOM are destroyed`)
  let after;for(let j=0;j<80;j++){after=await memory();if(!after.some(p=>p.pid===old))break;await sleep(50)}
  check(!after.some(p=>p.pid===old),`tray ${i}: renderer process is released, preserving background memory optimization`);report.memory.push({before,after})
  const next=app.waitForEvent('window',{timeout:20000}),started=Date.now();await app.evaluate(()=>global.__background.getTray().emit('click'));page=await next;await ready();report.restores.push({readyMs:Date.now()-started,opticsPrimed:await page.evaluate(()=>window.cinema.system('sessionGet').then(s=>s.opticsPrimed))})
  check(await app.evaluate(()=>!global.__background.getWindow().isVisible()),`tray ${i}: restore test also stays hidden and never steals focus`)
 }
 await page.screenshot({path:path.join(out,'logs-offscreen.png')});report.passed=true
}catch(error){report.error=error.stack;throw error}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report},null,2))}
