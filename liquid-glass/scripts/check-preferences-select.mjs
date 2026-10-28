import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'

const {_electron}=runtime('playwright'),out=path.resolve('artifacts/preferences-select')
const dpi=process.argv.find(a=>a.startsWith('--dpi='))?.split('=')[1]||'1'
await fs.mkdir(out,{recursive:true})
const profile=path.join(out,'profile-'+dpi+'-'+Date.now()),mock=path.join(out,'login-mock-'+dpi+'.cjs')
// Stub the native login-item methods before renderer IPC begins. Tests never
// add, remove or disable a real Windows startup entry.
await fs.writeFile(mock,`const fs=require('node:fs'),path=require('node:path');module.exports=({app})=>{
 const file=path.join(app.getPath('userData'),'fixture-login-item.json');let enabled=false;
 try{enabled=JSON.parse(fs.readFileSync(file,'utf8')).enabled}catch{}
 app.__loginCalls=[];app.__rejectLogin=false;
 app.getLoginItemSettings=()=>({openAtLogin:enabled,launchItems:enabled?[{name:'DanmuCinema',scope:'user',enabled:true}]:[]});
 app.setLoginItemSettings=settings=>{if(app.__rejectLogin)throw new Error('模拟启动项写入失败');app.__loginCalls.push(settings);enabled=settings.openAtLogin;fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,JSON.stringify({enabled}))};
}`)
const env={...process.env,DANMU_ELECTRON_USER_DATA:profile,DANMU_QA_SCRIPT:mock};delete env.ELECTRON_RUN_AS_NODE
const launch=()=>_electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${dpi}`],env})
const report={passed:false,dpi,cases:[],menus:[],errors:[]},check=(value,label)=>{assert(value,label);report.cases.push(label)}
let app=await launch()
async function prepare(){
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()})
 await page.locator('.sidebar').waitFor();return page
}
let page=await prepare()
const navigate=async name=>{await page.locator('.sidebar').getByRole('button',{name,exact:true}).click();await page.getByRole('heading',{name,exact:true}).waitFor()}
const startup=()=>page.locator('.preference-row').filter({hasText:'开机自动启动'}).getByRole('checkbox')
const waitStartup=()=>page.waitForFunction(()=>{const input=document.querySelector('.preference-row input');return input&&!input.disabled})
const waitChecked=value=>page.waitForFunction(value=>{const input=document.querySelector('.preference-row input');return input&&!input.disabled&&input.checked===value},value)
try{
 await navigate('启动与偏好');await waitStartup();check(!await startup().isChecked(),'隔离启动项初始关闭')
 await startup().click();await waitChecked(true)
 await navigate('缓存管理');await navigate('启动与偏好');await waitStartup()
 check(await startup().isChecked(),'勾选后无需保存，切换页面仍保留')
 const calls=await app.evaluate(({app})=>app.__loginCalls)
 check(calls.length===1&&calls[0].openAtLogin&&calls[0].enabled&&calls[0].name==='DanmuCinema'&&calls[0].args.includes('--tray')&&calls[0].args.includes('--start'),'勾选立即写入静默启动项，并启用系统启动批准状态')
 await page.reload();await page.locator('.preference-row').first().waitFor();await waitStartup();check(await startup().isChecked(),'重建渲染页面后从系统状态恢复勾选')
 await app.close();app=await launch();page=await prepare();await navigate('启动与偏好');await waitStartup()
 check(await startup().isChecked(),'冷启动后重新读取已保存的启动项')
 await app.evaluate(({app})=>{app.__rejectLogin=true});await startup().click();await page.getByText(/模拟启动项写入失败/).waitFor();await waitStartup()
 check(await startup().isChecked(),'系统写入失败时保留原状态并显示错误')
 await app.evaluate(({app})=>{app.__rejectLogin=false});await startup().click();await waitChecked(false)
 await navigate('服务总览');await navigate('启动与偏好');await waitStartup();check(!await startup().isChecked(),'取消勾选立即保存，切换页面不恢复旧值')
 await page.getByRole('button',{name:'保存偏好',exact:true}).click();await page.getByText('偏好设置已保存',{exact:true}).waitFor()
 check((await app.evaluate(({app})=>app.__loginCalls)).length===1,'保存其他偏好不会重复修改启动项')
 async function menu(trigger,label,stress=false){
  await page.mouse.move(2,2);await trigger.click()
  const list=page.getByRole('listbox',{name:label,exact:true});await list.waitFor();await page.waitForTimeout(230)
  const selected=list.locator('[role=option][aria-selected=true]');check(await selected.count()===1,label+'首次展开仅有一个已选项')
  const color=()=>selected.locator(':scope > .glass-material').evaluate(el=>getComputedStyle(el).backgroundColor)
  const initial=await color(),fill=await selected.evaluate(el=>getComputedStyle(el).getPropertyValue('--material-fill').trim())
  check(fill==='#55ab9280',label+'首次展开即显示绿色选中背景')
  const geometry=await list.evaluate(el=>{const r=el.getBoundingClientRect();return{top:r.top,bottom:r.bottom,left:r.left,right:r.right,scroll:el.scrollTop,page:document.querySelector('.page-content').scrollTop}})
  const visible=await list.locator('[role=option]').evaluateAll((elements,box)=>elements.map(el=>{const r=el.getBoundingClientRect();return{selected:el.getAttribute('aria-selected')==='true',x:r.x+Math.min(30,r.width/2),y:(Math.max(r.top,box.top+2)+Math.min(r.bottom,box.bottom-2))/2,partial:r.top<box.top||r.bottom>box.bottom,visible:r.bottom>box.top+3&&r.top<box.bottom-3}}).filter(row=>row.visible),geometry)
  const target=(stress?visible.find(row=>row.partial):null)||visible.find(row=>!row.selected)||visible[0]
  await page.mouse.move(target.x,target.y,{steps:5});await page.waitForTimeout(260)
  const after=await list.evaluate(el=>({top:el.getBoundingClientRect().top,scroll:el.scrollTop,page:document.querySelector('.page-content').scrollTop}))
  check(Math.abs(after.scroll-geometry.scroll)<.6&&Math.abs(after.top-geometry.top)<.6&&Math.abs(after.page-geometry.page)<.6,label+'鼠标移入选项不触发滚动或位置跳变')
  check(await color()===initial,label+'移开已选项后选中颜色保持一致')
  if(stress){
   await page.screenshot({path:path.join(out,`schedule-${dpi}.png`)})
   const beforeWheel=await list.evaluate(el=>el.scrollTop);await page.mouse.wheel(0,180);await page.waitForTimeout(160)
   check(await list.evaluate(el=>el.scrollTop)>beforeWheel,'长列表仍支持鼠标滚轮滚动')
   await trigger.focus();await page.keyboard.press('Home');await page.waitForTimeout(120)
   const firstVisible=await list.evaluate(el=>{const r=el.querySelector('[role=option]').getBoundingClientRect(),b=el.getBoundingClientRect();return r.top>=b.top-1&&r.bottom<=b.bottom+1})
   check(firstVisible,'Home 将首项滚入菜单视野')
   await page.keyboard.press('End');await page.waitForTimeout(120)
   const keyboard=await list.evaluate(el=>{const active=el.querySelector('.is-active'),r=active.getBoundingClientRect(),b=el.getBoundingClientRect();return{visible:r.top>=b.top-1&&r.bottom<=b.bottom+1,page:document.querySelector('.page-content').scrollTop}})
   check(keyboard.visible&&Math.abs(keyboard.page-geometry.page)<.6,'End 可将末项滚入视野且页面保持原位')
  }
  report.menus.push({label,initial,geometry,after})
  await page.keyboard.press('Escape');await list.waitFor({state:'detached'})
 }
 if(!process.argv.includes('--startup-only'))for(const zoom of [1,1.2]){
  await app.evaluate(({BrowserWindow},zoom)=>BrowserWindow.getAllWindows()[0].webContents.setZoomFactor(zoom),zoom)
  await navigate('缓存管理');await menu(page.getByRole('combobox',{name:'统一缓存有效期',exact:true}),'统一缓存有效期')
  await menu(page.getByRole('combobox',{name:'缓存类型',exact:true}),'缓存类型')
  await navigate('定时任务');await menu(page.getByRole('combobox',{name:'到时操作',exact:true}),'到时操作',true)
  await menu(page.getByRole('combobox',{name:'计时方式',exact:true}),'计时方式')
  await navigate('下载任务');await menu(page.getByRole('combobox',{name:'筛选任务',exact:true}),'筛选任务')
  await navigate('首次设置');await menu(page.getByRole('combobox',{name:'识别方式',exact:true}),'识别方式')
  await page.getByRole('button',{name:'管理媒体库',exact:true}).click();const manager=page.getByRole('dialog',{name:'管理媒体库',exact:true});await manager.waitFor()
  await menu(manager.getByRole('combobox',{name:'媒体库',exact:true}),'媒体库')
  await menu(manager.getByRole('combobox',{name:'媒体库识别方式',exact:true}),'媒体库识别方式')
  await manager.getByRole('button',{name:'关闭此页面',exact:true}).click();await manager.waitFor({state:'detached'})
  await navigate('连接设备');await menu(page.getByRole('combobox',{name:'局域网网卡',exact:true}),'局域网网卡')
  await navigate('启动与偏好');await menu(page.getByRole('combobox',{name:'界面缩放',exact:true}),'界面缩放')
  await navigate('影片与弹幕');await menu(page.getByRole('combobox',{name:'排序方式',exact:true}),'排序方式')
  await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();const match=page.getByRole('dialog',{name:'选择弹幕来源',exact:true});await match.waitFor()
  await menu(match.getByRole('combobox',{name:'接口来源',exact:true}),'接口来源')
  await menu(match.getByRole('combobox',{name:'本地目标文件夹或影片',exact:true}),'本地目标文件夹或影片')
  await match.getByRole('button',{name:'关闭此页面',exact:true}).click();await match.waitFor({state:'detached'})
 }
 check(report.errors.length===0,'本次操作无渲染异常');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{
 await fs.writeFile(path.join(out,`result-${dpi}.json`),JSON.stringify(report,null,2));await app.close()
}
console.log(JSON.stringify({passed:report.passed,dpi,cases:report.cases.length,menus:report.menus.length}))
