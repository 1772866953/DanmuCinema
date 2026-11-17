import fs from 'node:fs/promises'
import path from 'node:path'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/schedule-ui-'+Date.now())
await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa'],env})
const report={passed:false,checks:[]},check=(value,text)=>{assert(value,text);report.checks.push(text)}
try{
 const page=await app.firstWindow();await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 await page.getByRole('tab',{name:'定时任务',exact:true}).click();await page.getByRole('heading',{name:'定时任务',exact:true}).waitFor()
 const restore=plan=>page.evaluate(async plan=>{const state=await window.cinema.call('state');window.__qa.setState({...state,schedule:{active:true,status:'系统计划已恢复',remaining:'00:23:00',target:'2026-11-09 21:00:00',...plan}})},plan)
 await restore({id:'weekly-restored',mode:'weekly',action:5,daysMask:5,time:'21:00:00',awake:true})
 const mode=page.getByRole('combobox',{name:'计时方式',exact:true}),action=page.getByRole('combobox',{name:'到时操作',exact:true})
 await page.waitForFunction(()=>document.querySelector('[aria-label="计时方式"]').textContent.includes('每周计划'))
 check((await action.innerText()).includes('锁定'),'restored weekly action replaces stale local form drafts')
 check(await mode.isDisabled()&&await action.isDisabled(),'an active system plan cannot be silently edited')
 for(const [day,selected] of [['周一',true],['周二',false],['周三',true],['周日',false]])check(await page.getByRole('button',{name:day,exact:true}).getAttribute('aria-pressed')===String(selected),`restored ${day} selection is ${selected}`)
 check(await page.getByRole('button',{name:'开始定时',exact:true}).isDisabled()&&await page.getByRole('button',{name:'取消定时',exact:true}).isEnabled(),'a restored plan exposes explicit cancellation')
 await restore({id:'delay-restored',mode:'delay',action:0,seconds:1800,awake:false})
 await page.waitForFunction(()=>document.querySelector('[aria-label="计时方式"]').textContent.includes('倒计时'))
 check(await page.getByRole('spinbutton',{name:'倒计时分钟',exact:true}).getAttribute('aria-valuenow')==='30','saved countdown duration is restored independently of the remaining progress')
 check(await page.locator('.schedule-status h2').innerText()==='00:23:00','restored remaining progress is shown without resetting to the original duration')
 await page.screenshot({path:path.join(out,'schedule-restored.png')});report.passed=true
}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({out,...report},null,2))
