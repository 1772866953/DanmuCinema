'use strict'
const {app,BrowserWindow,ipcMain,Tray,Menu,dialog,shell,clipboard,nativeTheme}=require('electron')
const path=require('node:path'),fs=require('node:fs'),{pathToFileURL}=require('node:url')
const {CoreBridge}=require('./bridge.cjs')
const base=path.resolve(__dirname,'..'),qa=process.argv.includes('--qa')
if(process.env.DANMU_ELECTRON_USER_DATA)app.setPath('userData',path.resolve(process.env.DANMU_ELECTRON_USER_DATA))
else if(app.isPackaged)app.setPath('userData',path.join(path.dirname(process.execPath),'data','electron'))
let win=null,tray=null,core=null,lastState=null,quitting=false,shutdown=null,geometry={},uiSession=null
const loginArgs=app.isPackaged?['--tray','--start']:[base,'--tray','--start']
const dataRoot=()=>app.isPackaged?path.dirname(process.execPath):path.join(base,'runtime-data')
const stateFile=()=>path.join(app.getPath('userData'),'window.json')
function persist(){try{fs.mkdirSync(app.getPath('userData'),{recursive:true});fs.writeFileSync(stateFile(),JSON.stringify(geometry))}catch{}}
function fitNativeBounds(window,target){let requested={x:target.x,y:target.y,width:target.width,height:target.height};for(let i=0;i<4;i++){window.setBounds(requested);const actual=window.getBounds();if(actual.width===target.width&&actual.height===target.height)break;requested={...requested,width:requested.width+target.width-actual.width,height:requested.height+target.height-actual.height}}}
const fixture=qa?require('../tests/fixture.cjs')():null
const allowed=new Set(['cover','state','start','stop','scan','refresh','library','select','selectClear','prepare','pause','resume','cancel','retry','search','identify','episodes','plan','planSelect','downloadBatch','downloadSingle','refreshDanmu','history','historyAdd','historyRemove','historyClear','cache','cacheRemove','cacheClear','cacheRetention','logs','logsClear','logRetention','sources','preferences','initialize','addLibrary','libraries','removeLibrary','libraryType','connection','schedule','scheduleCancel','deletePlan','deleteConfirm','firewall'])
function trusted(event){if(!win||event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)throw new Error('不允许的调用来源')}
function remember(){if(!win||win.isDestroyed()||win.isMinimized())return;geometry={...win.getNormalBounds(),maximized:win.isMaximized()};persist()}
function show(){if(quitting)return;if(win&&!win.isDestroyed()){if(win.isMinimized())win.restore();win.show();win.focus();return}
  const initialGeometry={...geometry}
  win=new BrowserWindow({width:geometry.width||1320,height:geometry.height||880,minWidth:940,minHeight:640,...(Number.isFinite(geometry.x)?{x:geometry.x,y:geometry.y}:{}),show:false,frame:false,backgroundColor:'#e6edf1',icon:path.join(base,'public/icon.ico'),roundedCorners:true,webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,backgroundThrottling:true}})
  win.setMenu(null);win.loadFile(path.join(base,'dist/index.html'),qa?{hash:'qa'}:{})
  win.once('ready-to-show',()=>{if(initialGeometry.width&&initialGeometry.height)fitNativeBounds(win,initialGeometry);if(initialGeometry.maximized)win.maximize();if(!qa)win.show()})
  win.on('resize',()=>{remember();win?.webContents.send('window:state',{maximized:win.isMaximized()})});win.on('move',remember)
  win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',(event,url)=>{if(url!==pathToFileURL(path.join(base,'dist/index.html')).href)event.preventDefault()})
  win.on('close',event=>{remember();if(quitting||qa)return;if(lastState?.settings?.CloseToTray!==false){win.webContents.send('window:state',{releasing:true});return}event.preventDefault();quit()})
  const visibility=visible=>{if(core&&!quitting)core.ready.then(()=>{if(!quitting&&core.child.exitCode===null)return core.call('visibility',{visible})}).catch(()=>{})}
  win.on('show',()=>visibility(true));win.on('hide',()=>visibility(false));win.on('minimize',()=>visibility(false));win.on('restore',()=>visibility(true))
  win.on('closed',()=>{visibility(false);win=null})
}
const trace=phase=>{if(process.env.DANMU_LIFECYCLE_TRACE)fs.appendFileSync(process.env.DANMU_LIFECYCLE_TRACE,Date.now()+' '+phase+'\n')}
async function quit(confirmed=false){if(shutdown)return shutdown;if(!confirmed&&!qa&&(lastState?.schedule?.active||lastState?.batchRunning||lastState?.preparing||lastState?.loading)){show();const send=()=>win?.webContents.send('window:state',{requestExit:true});if(win.webContents.isLoading())win.webContents.once('did-finish-load',send);else send();return}quitting=true;trace('quit-request');shutdown=(async()=>{try{trace('core-stop');await core?.close();trace('core-stopped')}catch(error){trace('core-error:'+error.message)}tray?.destroy();win?.destroy();trace('app-quit');app.quit()})();return shutdown}
if(!qa&&!app.requestSingleInstanceLock()){app.quit()}else{
app.on('second-instance',show)
app.whenReady().then(async()=>{
  nativeTheme.themeSource='light';try{geometry=JSON.parse(fs.readFileSync(stateFile(),'utf8'))}catch{}
  if(!qa){
    const engine=app.isPackaged?path.join(process.resourcesPath,'engine'):path.join(base,'development'),data=dataRoot()
    if(app.isPackaged){const target=path.join(data,'data/jellyfin/plugins');fs.mkdirSync(target,{recursive:true});const plugins=path.join(engine,'plugins');if(fs.existsSync(plugins))for(const name of fs.readdirSync(plugins)){const destination=path.join(target,name);if(!fs.existsSync(destination))fs.cpSync(path.join(plugins,name),destination,{recursive:true})}const scripts=path.join(data,'scripts');fs.mkdirSync(scripts,{recursive:true});fs.copyFileSync(path.join(engine,'scripts/configure-firewall.ps1'),path.join(scripts,'configure-firewall.ps1'))}
    core=new CoreBridge(path.join(engine,app.isPackaged?'bin/DanmuCinema.exe':'bin/installer-app/DanmuCinema.exe'),{DANMU_DATA_ROOT:data,DANMU_RUNTIME_ROOT:app.isPackaged?path.join(engine,'runtime/jellyfin'):path.join(base,'runtime/jellyfin'),DANMU_LAUNCHER:app.isPackaged?process.execPath:'',DANMU_START_SERVICES:process.argv.includes('--start')?'1':'0'})
    core.on('state',state=>{lastState=state;win?.webContents.send('core:state',state);tray?.setToolTip(state.running?'弹幕影院 · 服务运行中':'弹幕影院 · 服务已停止')})
    core.on('foreground',show);core.on('offline',message=>{win?.webContents.send('core:state',{...lastState,offline:message});if(!quitting)dialog.showErrorBox('媒体核心停止',message)})
    core.ready.catch(error=>dialog.showErrorBox('启动失败',error.message))
  }
  tray=new Tray(path.join(base,'public/icon.ico'));tray.setToolTip('DanmuCinema · 弹幕影院');tray.on('double-click',show)
  tray.setContextMenu(Menu.buildFromTemplate([{label:'打开弹幕影院',click:show},{label:'启动服务',click:()=>core?.call('start').catch(()=>{})},{label:'停止服务',click:()=>core?.call('stop').catch(()=>{})},{type:'separator'},{label:'取消定时',click:()=>core?.call('scheduleCancel')},{label:'退出并停止服务',click:()=>quit()}]))
  ipcMain.handle('core:call',async(event,method,params)=>{trusted(event);if(!allowed.has(method))throw new Error('不支持的操作');if(qa){const result=await fixture.call(method,params);lastState=fixture.snap();win?.webContents.send('core:state',lastState);return result;}await core.ready;return core.call(method,params)})
  ipcMain.handle('window:action',async(event,action)=>{trusted(event);if(action==='minimize')win.minimize();else if(action==='maximize')win.isMaximized()?win.unmaximize():win.maximize();else if(action==='close')win.close();else if(action==='state')return{maximized:win.isMaximized()};else if(action==='scale100'||action==='scale120')win.webContents.setZoomFactor(action==='scale120'?1.2:1)})
  ipcMain.handle('system:action',async(event,action,value)=>{trusted(event)
    if(action==='chooseFolder'){const result=await dialog.showOpenDialog(win,{properties:['openDirectory']});return result.canceled?null:result.filePaths[0]}
    if(action==='copy'){clipboard.writeText(String(value));return true}
    if(action==='openFolder'){const target=lastState?.settings?.MediaFolder;if(!target)throw new Error('尚未设置媒体目录');return shell.openPath(target)}
    if(action==='openLogs')return shell.openPath(path.join(dataRoot(),'data'))
    if(action==='openWeb')return shell.openExternal(`http://127.0.0.1:${lastState?.settings?.Port||8096}/web/`)
    if(action==='feedback')return shell.openExternal('mailto:lizimu.hai@outlook.com')
    if(action==='autoStartGet')return app.getLoginItemSettings({path:process.execPath,args:loginArgs}).openAtLogin
    if(action==='autoStart'){app.setLoginItemSettings({openAtLogin:!!value,path:process.execPath,args:loginArgs,name:'DanmuCinema'});return app.getLoginItemSettings({path:process.execPath,args:loginArgs}).openAtLogin}
    if(action==='quitConfirmed'){setImmediate(()=>quit(true));return true}
    if(action==='sessionGet')return uiSession
    if(action==='sessionSet'){uiSession={...uiSession,...value};return true}
  })
  if(!process.argv.includes('--tray'))show()
  if(qa)show()
  if(qa){const script=process.env.DANMU_QA_SCRIPT;if(script)require(script)({app,getWindow:()=>win,show,quit})}
})
app.on('window-all-closed',()=>{});app.on('before-quit',event=>{if(!quitting){event.preventDefault();quit()}})
}
