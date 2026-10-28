import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/tray-preferences');await fs.mkdir(out,{recursive:true})
const hook=path.join(out,'state-delay.cjs')
await fs.writeFile(hook,`const {ipcMain}=require('electron');module.exports=({app,getWindow})=>{
 app.__currentWindow=getWindow;app.__stateCalls=[];
 const original=ipcMain._invokeHandlers.get('core:call');app.__stateWaiters=[];app.__holdState=false;app.__failState=false;
 app.getLoginItemSettings=()=>({openAtLogin:false,launchItems:[]});
 ipcMain.removeHandler('core:call');ipcMain.handle('core:call',async(event,method,...args)=>{
  if(method==='state'&&app.__holdState)await new Promise(resolve=>app.__stateWaiters.push(resolve));
  if(method==='state'&&app.__failState){app.__failState=false;throw new Error('模拟配置读取失败')}
  app.__stateCalls.push({method,sender:event.sender.id,current:getWindow()?.webContents?.id,frame:event.senderFrame===getWindow()?.webContents?.mainFrame});
  return original(event,method,...args);
 });
 app.__releaseState=()=>{app.__holdState=false;for(const resolve of app.__stateWaiters.splice(0))resolve()};
}`)
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now()),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.5'],env})
const report={passed:false,cases:[],errors:[]},check=(value,name)=>{assert(value,name);report.cases.push(name)}
let page=await app.firstWindow();page.setDefaultTimeout(12000)
const errors=()=>page.on('pageerror',e=>report.errors.push(e.message));errors()
const service=()=>page.locator('.preference-row').filter({hasText:'启动时运行视频与弹幕服务'}).getByRole('checkbox')
async function closeAndRestore(maximized=false,fail=false){
 await page.waitForFunction(async()=>localStorage.getItem('cinema.ui.page')==='"settings"'&&(await window.cinema.system('sessionGet'))?.page==='settings')
 await app.evaluate(({app,BrowserWindow},{maximized,fail})=>{app.__holdState=true;app.__failState=fail;const w=BrowserWindow.getAllWindows()[0];if(maximized)w.maximize();else if(w.isMaximized())w.unmaximize();w.close()},{maximized,fail})
 const next=app.waitForEvent('window');await app.evaluate(({app})=>app.emit('second-instance'));page=await next;page.setDefaultTimeout(12000);errors()
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()})
 await page.getByRole('heading',{name:'启动与偏好',exact:true}).waitFor();await page.waitForTimeout(150)
 check(await page.locator('.preference-row input').count()===0,'配置返回前不显示默认未勾选的复选框')
 check(await page.getByRole('button',{name:'保存偏好',exact:true}).count()===0,'配置未就绪时无法保存默认值覆盖真实设置')
 await app.evaluate(({app})=>app.__releaseState())
}
try{
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 await page.evaluate(()=>window.cinema.call('preferences',{launch:true,closeToTray:true,original:false,port:9138,danmuPort:9328,interval:7,scale:100}))
 await page.locator('.sidebar').getByRole('button',{name:'启动与偏好',exact:true}).click();await service().waitFor();check(await service().isChecked(),'保存的启动服务选项初始为勾选')
 for(const maximized of [false,true,false]){
  await closeAndRestore(maximized);await service().waitFor();check(await service().isChecked(),'从托盘恢复后首次打开设置就显示已保存的勾选')
  check(await page.getByLabel('视频服务端口',{exact:true}).inputValue()==='9138'&&await page.getByLabel('弹幕服务端口',{exact:true}).inputValue()==='9328','其他偏好字段也使用后台配置初始化')
  await page.locator('.sidebar').getByRole('button',{name:'缓存管理',exact:true}).click();await page.locator('.sidebar').getByRole('button',{name:'启动与偏好',exact:true}).click();await service().waitFor();check(await service().isChecked(),'切换页面前后勾选一致')
 }
 await service().uncheck();await page.evaluate(()=>window.cinema.call('state'));await page.waitForTimeout(80);check(!await service().isChecked(),'后台状态刷新不会覆盖用户尚未保存的勾选修改')
 await page.getByRole('button',{name:'保存偏好',exact:true}).click();await page.getByText('偏好设置已保存',{exact:true}).waitFor()
 await closeAndRestore();await service().waitFor();check(!await service().isChecked(),'保存为未勾选后，托盘恢复正确保留未勾选')
 await closeAndRestore(false,true);await page.getByText(/模拟配置读取失败/).waitFor();check(await page.getByRole('button',{name:'保存偏好',exact:true}).count()===0,'读取失败时保持错误状态，不开放默认设置表单')
 await page.getByRole('button',{name:'重新读取',exact:true}).click();await service().waitFor();check(!await service().isChecked(),'读取失败后可重试并恢复实际保存值')
 await page.screenshot({path:path.join(out,'restored.png')});check(report.errors.length===0,'本次恢复与设置读取没有渲染异常');report.passed=true
}catch(error){report.errors.push(error.stack);try{report.mainDiagnostic=await app.evaluate(({app,BrowserWindow})=>({current:app.__currentWindow()?.id,all:BrowserWindow.getAllWindows().map(w=>({id:w.id,webContents:w.webContents.id})),calls:app.__stateCalls}));await page.screenshot({path:path.join(out,'failure.png')});report.diagnostic=await page.evaluate(()=>({text:document.body.innerText.slice(0,1200),page:localStorage.getItem('cinema.ui.page')}))}catch{}throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.evaluate(({app})=>app.__releaseState()).catch(()=>{});await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length}))
