import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/file-pairing-ui-'+Date.now()),profile=path.join(out,'profile'),report={passed:false,checks:[],errors:[]}
await fs.mkdir(profile,{recursive:true});await fs.writeFile(path.join(profile,'window.json'),JSON.stringify({width:1320,height:880,maximized:true}))
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_FIXTURE:path.resolve('tests/file-pairing-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa'],env,timeout:30000})
const check=(v,s)=>{assert(v,s);report.checks.push(s)}
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.getByRole('button',{name:'文件名配对',exact:true}).click();const dialog=page.getByRole('dialog',{name:'文件名配对',exact:true});await dialog.waitFor();await page.waitForTimeout(350)
 check(await dialog.getByRole('button',{name:'执行重命名',exact:true}).isDisabled(),'apply requires an inspected preview')
 const choose=async(paths,index,folder=true)=>{await app.evaluate(({dialog},paths)=>{dialog.showOpenDialog=async()=>({canceled:false,filePaths:paths})},paths);await dialog.locator('.pairing-source').nth(index).getByRole('button',{name:folder?'选择目录':'选择文件',exact:true}).click()}
 await choose(['D:\\示例视频'],0);await choose(['D:\\示例弹幕'],1);await page.waitForFunction(()=>document.querySelectorAll('.pairing-table tbody tr').length===12);check((await dialog.textContent()).includes('已配对 12 / 12'),'root folder selections build an editable paired list')
 const combo=dialog.getByRole('combobox',{name:'为 原弹幕 01 集.xml 配对视频',exact:true});await combo.click();await page.getByRole('option',{name:'不处理',exact:true}).click();await dialog.getByRole('button',{name:'检查并预览',exact:true}).click();await dialog.locator('.pairing-preview').waitFor();check((await dialog.locator('.pairing-preview>strong').textContent()).includes('11'),'unpaired rows are excluded')
 await dialog.getByRole('checkbox',{name:'将配对的弹幕 / 字幕移至视频目录',exact:true}).check();check(await dialog.getByRole('button',{name:'执行重命名',exact:true}).isDisabled(),'location change invalidates the previous plan')
 await dialog.getByRole('button',{name:'检查并预览',exact:true}).click();await dialog.locator('.pairing-preview').waitFor();const state=await page.evaluate(()=>window.cinema.call('state'));check(state.qaRenameCalls.at(-1).placeWithVideo===true,'optional placement is sent explicitly')
 await dialog.getByRole('combobox',{name:'重命名方向',exact:true}).click();await page.getByRole('option',{name:'视频跟随弹幕 / 字幕',exact:true}).click();check(await dialog.getByRole('button',{name:'执行重命名',exact:true}).isDisabled(),'direction change also invalidates the plan');await dialog.getByRole('button',{name:'检查并预览',exact:true}).click();await dialog.locator('.pairing-preview').waitFor();check((await dialog.locator('.pairing-preview').textContent()).includes('原弹幕 02 集.mkv'),'reverse mode previews the video filename')
 await dialog.getByRole('button',{name:'执行重命名',exact:true}).click();await dialog.getByRole('status').waitFor();check((await dialog.getByRole('status').textContent()).includes('11'),'apply reports completion and updates paths')
 await choose(Array.from({length:30},(_,i)=>`D:\\弹幕\\${i+1}.xml`),1,false);await page.waitForFunction(()=>document.querySelectorAll('.pairing-table tbody tr').length===25);await dialog.getByRole('button',{name:'下一页配对',exact:true}).click();check(await dialog.locator('tbody tr').count()===5,'large batches paginate instead of mounting all row controls')
 const layout=await dialog.evaluate(e=>({rect:e.getBoundingClientRect().toJSON(),inner:[...e.querySelectorAll('.pairing-source,.pairing-table,.modal-actions')].map(x=>x.getBoundingClientRect().toJSON()),footer:[...e.querySelectorAll('.modal-actions button')].map(x=>x.getBoundingClientRect().toJSON()),width:innerWidth,height:innerHeight}));check(layout.rect.left>=0&&layout.rect.right<=layout.width+1&&layout.inner.every(r=>r.left>=layout.rect.left&&r.right<=layout.rect.right+1),'pairing controls fit the dialog width');check(layout.footer.every(r=>r.top>=layout.rect.top&&r.bottom<=Math.min(layout.rect.bottom,layout.height)-5),'action buttons remain visible outside the scrolling content')
 for(const zoom of [1,1.2]){
  await app.evaluate(({BrowserWindow},zoom)=>{const w=BrowserWindow.getAllWindows()[0];w.unmaximize();w.setSize(940,640);w.webContents.setZoomFactor(zoom)},zoom)
  await page.waitForTimeout(200)
  const fit=await dialog.evaluate(e=>{const r=e.getBoundingClientRect(),footer=e.querySelector('.modal-actions').getBoundingClientRect(),content=e.querySelector('.pairing-content');return{inViewport:r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1,footerVisible:footer.top>=r.top&&footer.bottom<=r.bottom-5,noHorizontalOverflow:content.scrollWidth<=content.clientWidth+1}})
  check(fit.inViewport&&fit.footerVisible&&fit.noHorizontalOverflow,`minimum window at ${zoom*100}% keeps content and actions within bounds`)
 }
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.webContents.setZoomFactor(1);w.maximize()})
 await dialog.locator('.pairing-content').evaluate(e=>e.scrollTop=0);await page.waitForTimeout(100)
 const png=await app.evaluate(async({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.capturePage().then(i=>i.toPNG().toString('base64')));await fs.writeFile(path.join(out,'pairing.png'),Buffer.from(png,'base64'));check(report.errors.length===0,'no renderer errors');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report}))}
