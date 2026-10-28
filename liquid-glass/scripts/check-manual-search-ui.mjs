import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/manual-search-ui');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.5'],env})
const report={passed:false,cases:[],errors:[]},check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click()
 await page.locator('tbody tr').first().getByRole('button',{name:'星海旅行 第一季',exact:true}).dblclick();await page.waitForFunction(()=>document.querySelectorAll('tbody tr').length===12)
 await page.locator('thead').getByRole('checkbox').click();await page.getByRole('button',{name:'匹配已选影片',exact:true}).waitFor()
 await page.locator('tbody tr').nth(9).locator('.danmu-button').click()
 const match=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await match.waitFor()
 check((await page.evaluate(()=>window.cinema.call('state'))).selected===1,'点击单个影片时仅选择该文件，不沿用此前12集选择')
 check((await match.getByRole('textbox',{name:'输入动漫或影片名称'}).inputValue()).includes('S1 · 第 10 集'),'单集搜索框保留提取后的季号和第10集')
 await match.getByRole('button',{name:'联合搜索',exact:true}).click();await match.getByText('优先度 100/100',{exact:true}).waitFor()
 const choices=match.getByRole('listbox',{name:'作品搜索结果'}).getByRole('option')
 check((await choices.first().innerText()).includes('100/100')&&(await choices.nth(1).innerText()).includes('72/100'),'搜索列表每项显示优先度，并按高到低展示')
 check((await choices.first().innerText()).includes('哈希精确命中'),'搜索结果展示可解释的匹配依据')
 check((await choices.first().locator('.match-priority').getAttribute('title')).includes('不是准确率'),'分值提示不将排序分数当作识别概率')
 await choices.first().click();const online=match.getByRole('listbox',{name:'在线剧集'});await online.getByRole('option').nth(11).waitFor()
 check(await online.getByRole('option').nth(9).getAttribute('aria-selected')==='true','读取在线剧集后自动定位第10集')
 await page.waitForFunction(()=>{const list=document.querySelector('[aria-label="在线剧集"]'),row=list?.querySelector('[aria-selected="true"]');if(!row)return false;const r=row.getBoundingClientRect(),b=list.getBoundingClientRect();return r.top>=b.top-1&&r.bottom<=b.bottom+1})
 check(true,'自动定位的单集滚入当前剧集列表视野，页面本身不被滚动')
 check(await match.getByRole('button',{name:'下载所选单集',exact:true}).isEnabled(),'单集定位后可直接核对并下载')
 await page.mouse.move(2,2);await page.screenshot({path:path.join(out,'single.png')})
 await match.getByRole('button',{name:'关闭此页面',exact:true}).click();await match.waitFor({state:'detached'})
 await page.locator('.breadcrumb').getByRole('button',{name:'媒体库',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('tbody tr').length===3)
 await page.locator('tbody tr').first().locator('.danmu-button').click();await match.waitFor()
 check((await page.evaluate(()=>window.cinema.call('state'))).selected===12,'文件夹入口仍选中整个季度12集')
 await match.getByRole('button',{name:'联合搜索',exact:true}).click();await choices.first().waitFor();await choices.first().click();await online.getByRole('option').nth(11).waitFor()
 await match.getByRole('button',{name:'预览已选影片并下载',exact:true}).click();const batch=page.getByRole('dialog',{name:'预览已选影片并下载',exact:true});await batch.waitFor()
 check(await batch.locator('tbody tr').count()===12&&await batch.locator('tbody input:checked').count()===12,'整季预览保留12个逐集对应且默认勾选的影片')
 await batch.getByRole('button',{name:'关闭此页面',exact:true}).click();await batch.waitFor({state:'detached'})
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(1.2));await page.waitForTimeout(250);await page.mouse.move(2,2)
 const fits=await choices.first().evaluate(el=>{const badge=el.querySelector('.match-priority').getBoundingClientRect(),row=el.getBoundingClientRect();return badge.left>=row.left&&badge.right<=row.right&&el.scrollWidth<=el.clientWidth+1})
 check(fits,'120%界面缩放及150%显示缩放下优先度标签不超出候选行')
 await page.screenshot({path:path.join(out,'season-120.png')});check(report.errors.length===0,'本次搜索和匹配界面无渲染异常');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,errors:report.errors}))
