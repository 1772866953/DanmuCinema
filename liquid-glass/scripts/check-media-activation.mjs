import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/media-activation-'+Date.now());await fs.mkdir(out,{recursive:true})
const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,`module.exports=api=>{const p=api.getPlayer();const{ipcMain}=require('electron');global.plays=[];ipcMain.removeHandler('player:action');ipcMain.handle('player:action',(e,action,value)=>{if(action==='open'){global.plays.push(value);return {active:false,settings:{}}}return p.action(action,value)})}`)
const env={...process.env,DANMU_QA_OFFSCREEN:'1',DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const packaged=process.env.DANMU_PACKAGED_ROOT,app=await _electron.launch({executablePath:packaged?path.join(packaged,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(packaged?[]:['.']),'--qa'],env}),report={passed:false,checks:[]}
const check=(v,s)=>{assert(v,s);report.checks.push(s)}
try{
 const page=await app.firstWindow();await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor();check(await page.locator('.media-gallery').getAttribute('title')===null,'the media area has no Ctrl-wheel hover tooltip')
 for(const view of ['posters','list']){
  await page.getByRole('button',{name:view==='posters'?'封面视图':'列表视图',exact:true}).click()
  const item=()=>page.locator(view==='posters'?'.poster-image-button':'.media-name-trigger').first()
  const folder=()=>view==='posters'?page.getByRole('button',{name:'打开 星海旅行 第一季',exact:true}):page.getByRole('button',{name:'星海旅行 第一季',exact:true})
  const before=await app.evaluate(()=>global.plays.length)
  await folder().click();await page.waitForTimeout(1200)
  check(await page.locator('.breadcrumb').innerText().then(t=>!t.includes('第一季'))&&await app.evaluate(()=>global.plays.length)===before,`${view}: a single folder click has no delayed action`)
  await folder().dblclick({delay:70});await page.waitForFunction(()=>document.querySelector('.breadcrumb')?.textContent.includes('第一季'))
  await page.waitForFunction(()=>document.querySelector('.poster-image-button[aria-label="打开 BD"],.media-name-trigger')?.textContent==='BD'||!!document.querySelector('.poster-image-button[aria-label="打开 BD"]'))
  check(await app.evaluate(()=>global.plays.length)===before,`${view}: a folder double-click navigates without playing`)
  await item().dblclick({delay:70});await page.waitForFunction(()=>document.querySelector('.breadcrumb')?.textContent.includes('BD'))
  const selectedBefore=(await page.evaluate(()=>window.cinema.call('state'))).selected
  await item().click();await page.waitForTimeout(1200)
  check(await app.evaluate(()=>global.plays.length)===before&&(await page.evaluate(()=>window.cinema.call('state'))).selected===selectedBefore,`${view}: a single episode click neither plays nor selects`)
  await item().dblclick({delay:70});await page.waitForFunction(()=>true)
  await page.waitForTimeout(120)
  check(await app.evaluate(()=>global.plays.length)===before+1,`${view}: the second episode click dispatches playback without a single-click timeout`)
  check((await app.evaluate(()=>global.plays.at(-1))).siblings===true,`${view}: episode playback requests its siblings immediately`)
  await page.locator(view==='posters'?'.poster-description strong':'.media-table tbody tr td:nth-child(4)').first().dblclick({delay:70});await page.waitForTimeout(120)
  check(await app.evaluate(()=>global.plays.length)===before+2,`${view}: double-clicking the caption or row body also plays immediately`)
  check((await page.evaluate(()=>window.cinema.call('state'))).selected===selectedBefore,`${view}: an episode double-click does not trigger single-click selection`)
  const checkbox=page.locator(view==='posters'?'.poster-check input':'.media-table tbody input').first()
  await checkbox.check();check(await checkbox.isChecked(),`${view}: a checkbox responds to a single click`)
  await checkbox.dblclick({force:true});await page.waitForTimeout(150)
  check(await app.evaluate(()=>global.plays.length)===before+2,`${view}: double-clicking a checkbox cannot start playback`)
  check(await page.getByRole('dialog').count()===0,`${view}: item activation never opens the match dialog`)
  await page.getByRole('button',{name:'取消选择',exact:true}).click()
  await page.locator('.breadcrumb button').first().click();await page.waitForTimeout(250)
 }
 await page.getByRole('button',{name:'本地播放设置',exact:true}).click();const dialog=page.getByRole('dialog',{name:'本地播放与弹幕',exact:true});await dialog.waitFor();check(await dialog.getByRole('slider',{name:'播放预读预算',exact:true}).count()===1,'preload budget appears in playback settings')
 const budget=dialog.getByRole('spinbutton',{name:'播放预读预算数值',exact:true});await budget.fill('100');await budget.press('Enter');await page.waitForTimeout(200);check((await page.evaluate(()=>window.cinema.player('state'))).settings.preloadBudget===100,'preload budget is saved from the actual settings UI');await budget.scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,'settings.png')});report.passed=true
}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report},null,2))}
