import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/library-removal-ui'),profile=path.join(out,'profile-'+Date.now())
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:profile};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const report={passed:false,cases:[],errors:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
try {
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click()
 await page.locator('tbody tr').first().getByRole('button',{name:'星海旅行 第一季',exact:true}).dblclick();await page.locator('.breadcrumb').getByText('星海旅行 第一季',{exact:true}).waitFor()
 await page.getByRole('button',{name:'管理媒体库',exact:true}).click();const manager=page.getByRole('dialog',{name:'管理媒体库',exact:true});await manager.waitFor()
 check((await manager.locator('.library-directories').innerText()).includes('星海旅行 第一季'),'管理页显示当前媒体库的实际目录')
 await manager.getByRole('combobox',{name:'媒体库',exact:true}).click();await page.getByRole('option',{name:'其他影片',exact:true}).click()
 check((await manager.getByRole('combobox',{name:'媒体库识别方式',exact:true}).innerText()).includes('电视剧 / 动漫'),'切换媒体库时同步展示实际识别方式')
 await manager.getByRole('button',{name:'删除媒体库',exact:true}).click();let confirm=page.getByRole('dialog',{name:'确认操作',exact:true});await confirm.waitFor()
 const message=await confirm.innerText();check(message.includes('其他影片')&&message.includes('午后日记 第一季')&&message.includes('XML 弹幕及原文件夹均会保留'),'删除确认清楚展示库名、目录和保留媒体说明')
 await page.screenshot({path:path.join(out,'confirm.png')});await confirm.getByRole('button',{name:'取消',exact:true}).click();await confirm.waitFor({state:'detached'})
 check((await page.evaluate(()=>window.cinema.call('libraries'))).length===3,'取消确认不删除媒体库')
 check(await manager.isVisible(),'关闭确认子弹窗保留媒体库管理页')
 async function remove(){await manager.getByRole('button',{name:'删除媒体库',exact:true}).click();const confirm=page.getByRole('dialog',{name:'确认操作',exact:true});await confirm.waitFor();await confirm.getByRole('button',{name:'确认',exact:true}).click();await confirm.waitFor({state:'detached'});await page.waitForTimeout(180)}
 await remove();let libraries=await page.evaluate(()=>window.cinema.call('libraries'));check(libraries.length===2&&!libraries.some(x=>x.Name==='其他影片'),'确认后移除所选媒体库，其他库保留')
 check((await manager.getByRole('combobox',{name:'媒体库',exact:true}).innerText()).includes('我的影片'),'删除后自动选中剩余媒体库')
 await remove();libraries=await page.evaluate(()=>window.cinema.call('libraries'));check(libraries.length===1&&libraries[0].Name==='山间来信','可删除默认库并保留最后一个库')
 check(await page.evaluate(()=>JSON.parse(localStorage.getItem('cinema.ui.directory.media')||'""'))==='','删除后清理旧的文件夹导航位置')
 await remove();check((await page.evaluate(()=>window.cinema.call('libraries'))).length===0,'最后一个媒体库可以删除')
 check(await manager.getByRole('button',{name:'删除媒体库',exact:true}).isDisabled()&&await manager.getByRole('button',{name:'保存并重新扫描',exact:true}).isDisabled(),'无媒体库时禁用删除和保存操作')
 check((await manager.innerText()).includes('暂无媒体库'),'删除最后一库后显示添加媒体库提示')
 await page.screenshot({path:path.join(out,'empty.png')});await manager.getByRole('button',{name:'关闭此页面'}).click();await manager.waitFor({state:'detached'})
 check(await page.locator('tbody tr').count()===0,'主页面及时移除已删除库的影片列表')
 await page.locator('.sidebar').getByRole('button',{name:'首次设置',exact:true}).click();check(await page.getByRole('button',{name:'管理媒体库',exact:true}).isVisible(),'首次设置同样提供媒体库管理入口')
 check(report.errors.length===0,'新功能页面没有脚本异常');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,checks:report.cases},null,2))
