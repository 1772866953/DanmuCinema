import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),sharp=runtime('sharp'),out=path.resolve('artifacts/modal-lifecycle-'+Date.now()),report={passed:false,checks:[],errors:[]}
await fs.mkdir(out,{recursive:true})
const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,'module.exports=api=>global.__lifecycle=api')
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_SCRIPT:hook,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const packaged=process.env.DANMU_PACKAGED_ROOT
const app=await _electron.launch({executablePath:packaged?path.join(packaged,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(packaged?[]:['.']),'--qa'],env,timeout:40000}),check=(value,message)=>{assert(value,message);report.checks.push(message)}
try{
 let page=await app.firstWindow();page.setDefaultTimeout(18000);page.on('pageerror',error=>report.errors.push(error.message))
 const ready=async()=>{await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__lifecycle.getWindow();w.show();w.focus()})}
 await ready();await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click()
 const player=page.getByRole('dialog',{name:'本地播放与弹幕',exact:true}),sources=page.getByRole('dialog',{name:'管理接口',exact:true})
 for(const [button,dialog]of [['本地播放设置',player],['管理接口',sources]]){
  await page.getByRole('button',{name:button,exact:true}).click();await dialog.waitFor();await page.waitForTimeout(450)
  check(await dialog.evaluate(n=>document.activeElement===n),button+' receives focus on its first visible commit')
  await dialog.evaluate(n=>{n.querySelector('.modal-body').scrollTop=300})
  const before=await dialog.boundingBox(),header=await dialog.locator('.modal-header').boundingBox()
  await page.mouse.move(header.x+80,header.y+35);await page.mouse.down();await page.mouse.move(header.x+110,header.y+55,{steps:8});await page.mouse.up();await page.waitForTimeout(80)
  const moved=await dialog.boundingBox();check(moved.x-before.x>25&&moved.y-before.y>15,button+' retains header dragging')
  await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});await page.waitForTimeout(260)
  await page.getByRole('button',{name:button,exact:true}).click();await dialog.waitFor();await page.waitForTimeout(450)
  const reopened=await dialog.boundingBox();check(Math.abs(reopened.x-before.x)<1&&Math.abs(reopened.y-before.y)<1,button+' starts at original position after a completed close')
  check(await dialog.locator('.modal-body').evaluate(n=>n.scrollTop)===0,button+' starts at the top on reopen')
  for(let repeat=0;repeat<5;repeat++){
   await page.keyboard.press('Escape');await page.waitForTimeout(35)
   await page.getByRole('button',{name:button,exact:true}).evaluate(n=>n.click());await dialog.waitFor();await page.waitForTimeout(55)
  }
  await page.waitForTimeout(450);check(await dialog.evaluate(n=>Math.abs(new DOMMatrixReadOnly(getComputedStyle(n).transform).a-1)<.00001&&Number(getComputedStyle(n).opacity)===1),button+' rapid close/reopen finishes at a clear full-size pose')
  await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});await page.waitForTimeout(260)
 }
 await page.getByRole('button',{name:'管理接口',exact:true}).click();await sources.waitFor();await page.waitForTimeout(450)
 const enabled=sources.getByRole('checkbox').first(),original=await enabled.isChecked();await enabled.click();await page.keyboard.press('Escape');await sources.waitFor({state:'detached'});await page.waitForTimeout(260)
 await page.getByRole('button',{name:'管理接口',exact:true}).click();await sources.waitFor();check(await enabled.isChecked()===original,'closing without saving discards source edits');await page.keyboard.press('Escape');await sources.waitFor({state:'detached'});await page.waitForTimeout(300)
 await page.mouse.move(3,3);await page.waitForTimeout(450)
 const visible=await page.screenshot();const hide=await page.addStyleTag({content:'[data-retained-dialog]{visibility:hidden!important}'});await page.waitForTimeout(150);const hidden=await page.screenshot();await hide.evaluate(n=>n.remove())
 const a=await sharp(visible).raw().toBuffer(),b=await sharp(hidden).raw().toBuffer();let changes=0,max=0;for(let n=0;n<a.length;n++){const delta=Math.abs(a[n]-b[n]);max=Math.max(max,delta);if(delta)changes++}
 report.parkedPixels={changes,max};check(changes===0,'parked forms leave no visible pixels');await fs.writeFile(path.join(out,'parked.png'),visible)
 const parked=await page.locator('[data-retained-dialog]').evaluateAll(nodes=>nodes.map(n=>({name:n.dataset.retainedDialog,inert:n.inert,hidden:n.getAttribute('aria-hidden'),animations:n.getAnimations({subtree:true}).filter(a=>a.playState==='running').length})))
 check(parked.length===2&&new Set(parked.map(n=>n.name)).size===2&&parked.every(n=>n.inert&&n.hidden==='true'&&n.animations===0),'both parked forms are unique, inaccessible and have no animation loop')
 await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();const parent=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await parent.waitFor();await page.waitForTimeout(450);const bounds=await parent.boundingBox()
 await parent.getByRole('button',{name:'管理接口',exact:true}).click();await sources.waitFor();await page.waitForTimeout(450)
 check(await sources.evaluate(n=>!n.closest('.modal-layer').inert)&&await parent.evaluate(n=>n.closest('.modal-layer').inert),'nested dialog owns focus and blocks its parent')
 await page.keyboard.press('Escape');await sources.waitFor({state:'detached'});const after=await parent.boundingBox();check(await parent.isVisible()&&['x','y','width','height'].every(k=>Math.abs(bounds[k]-after[k])<.3),'nested ESC keeps the parent in place');check(await parent.evaluate(n=>n.contains(document.activeElement)),'nested ESC restores keyboard focus inside the parent');await page.keyboard.press('Escape');await parent.waitFor({state:'detached'})
 const pids=await app.evaluate(()=>global.__lifecycle.getWindow().webContents.mainFrame.framesInSubtree.map(f=>f.osProcessId))
 await app.evaluate(()=>global.__lifecycle.getWindow().close());let released=false
 for(let attempt=0;attempt<100;attempt++){released=!(await app.evaluate(({app})=>app.getAppMetrics())).some(p=>pids.includes(p.pid));if(released)break;await new Promise(resolve=>setTimeout(resolve,50))}
 check(released,'tray destroys both old renderers and their retained forms');const next=app.waitForEvent('window');await app.evaluate(()=>global.__lifecycle.show());page=await next;await ready()
 await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.getByRole('button',{name:'本地播放设置',exact:true}).click();await page.getByRole('dialog',{name:'本地播放与弹幕',exact:true}).waitFor();await page.keyboard.press('Escape');await page.getByRole('dialog',{name:'本地播放与弹幕',exact:true}).waitFor({state:'detached'})
 check(await page.locator('iframe,.dock-shader-canvas,.dock-rim-canvas').count()===0,'restored navigation uses the original in-document optical renderer')
 await page.getByRole('tab',{name:'定时任务',exact:true}).click();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor()
 check(report.errors.length===0,'no renderer errors');report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report},null,2))}
