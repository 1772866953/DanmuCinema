import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/liquid-input-ui-'+Date.now()),profile=path.join(out,'profile'),hook=path.join(out,'hook.cjs'),report={passed:false,checks:[],errors:[]}
await fs.mkdir(profile,{recursive:true});await fs.writeFile(hook,'module.exports=api=>global.__inputTest=api')
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_SCRIPT:hook,DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa'],env,timeout:40000})
const check=(value,label)=>{assert(value,label);report.checks.push(label)}
try{
 let page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));page.setDefaultTimeout(15000)
 const ready=async()=>{await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');await app.evaluate(()=>{const w=global.__inputTest.getWindow();w.show();w.focus()})}
 await ready()
 const exercise=async(button,label)=>{
  const box=await button.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();await page.mouse.move(box.x+box.width/2+38,box.y+box.height/2-18,{steps:8});await page.waitForTimeout(150)
  const held=await button.evaluate(el=>{const matrix=n=>{const m=new DOMMatrix(getComputedStyle(n).transform);return[m.a,m.b,m.c,m.d,m.e,m.f]};return{pressed:el.dataset.pressed,material:matrix(el.querySelector('.glass-material')),text:matrix(el.querySelector('.glass-content'))}})
  check(held.pressed==='true'&&held.material.every((v,i)=>Math.abs(v-held.text[i])<.0001),label+' keeps matching native foreground and material deformation')
  await page.mouse.up();await page.mouse.move(2,2);await page.waitForTimeout(500)
  check(await button.getAttribute('data-pressed')===null,label+' releases outside the original hit area')
 }
 await exercise(page.getByRole('button',{name:'连接设备',exact:true}),'normal button')
 await exercise(page.getByRole('button',{name:'反馈与帮助',exact:true}),'icon button')
 const nav=async name=>{await page.getByRole('tab',{name,exact:true}).click();await page.getByRole('heading',{name,exact:true}).waitFor();await page.waitForTimeout(250)}
 await nav('影片与弹幕');const button=page.getByRole('button',{name:'管理接口',exact:true}),b=await button.boundingBox()
 await page.mouse.move(b.x+b.width/2,b.y+b.height/2);await page.mouse.down();await page.waitForTimeout(80);await page.evaluate(()=>window.dispatchEvent(new Event('blur')))
 check(await button.getAttribute('data-pressed')===null,'window blur clears an active press');await page.mouse.up();await page.mouse.move(2,2)
 await page.mouse.move(b.x+b.width/2,b.y+b.height/2);await page.mouse.down();await page.evaluate(()=>document.getElementById('workspace-tab-schedule').click());await page.mouse.up();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor()
 check(await page.locator('.glass-button[data-pressed=true]').count()===0,'unmount during capture leaves no pressed control')
 const minutes=page.getByRole('spinbutton').first();await minutes.fill('47');await minutes.blur();await nav('启动与偏好');await nav('定时任务');check(await page.getByRole('spinbutton').first().inputValue()==='47','page revisit retains the edited draft')
 const pid=await app.evaluate(()=>global.__inputTest.getWindow().webContents.getOSProcessId()),next=app.waitForEvent('window',{timeout:30000})
 await app.evaluate(()=>{global.__inputTest.getWindow().close();global.__inputTest.show()});page=await next;await ready();await nav('定时任务')
 check(await page.getByRole('spinbutton').first().inputValue()==='47','tray renderer recreation retains the persisted draft')
 check(!(await app.evaluate(({app})=>app.getAppMetrics())).some(p=>p.pid===pid),'tray releases the old renderer')
 check(report.errors.length===0,'no renderer errors during cancellation and restoration');report.passed=true
}catch(error){report.errors.push(error.stack);process.exitCode=1}finally{await app.close();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}
