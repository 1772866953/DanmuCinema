if(process.argv.includes('--player-overlay')){require('./overlay-process.cjs');return}
'use strict'

const {app,BrowserWindow,ipcMain,Tray,Menu,dialog,shell,clipboard,nativeTheme,protocol,net}=require('electron')

const path=require('node:path'),fs=require('node:fs'),{pathToFileURL}=require('node:url')

const {CoreBridge}=require('./bridge.cjs')

const {createLoginItems}=require('./login-items.cjs')

const {LocalPlayer}=require('./player.cjs')

// Use the capable adapter on hybrid-GPU systems for high-refresh glass optics.

// This is a process preference; it does not change Windows graphics settings.

app.commandLine.appendSwitch('force_high_performance_gpu')

// The tray releases the renderer. Keep a restoring/priming window scheduled

// until its optics are ready, even when another application still covers it.

app.commandLine.appendSwitch('disable-renderer-backgrounding')

app.commandLine.appendSwitch('disable-backgrounding-occluded-windows')

app.commandLine.appendSwitch('disable-features',[...new Set([...app.commandLine.getSwitchValue('disable-features').split(',').filter(Boolean),'CalculateNativeWinOcclusion','UseEcoQoSForBackgroundProcess'])].join(','))

const base=path.resolve(__dirname,'..'),qa=process.argv.includes('--qa'),offscreenTest=qa&&process.env.DANMU_QA_OFFSCREEN==='1'
if(process.env.DANMU_ISOLATED_DOCK==='1'){
 app.commandLine.appendSwitch('site-per-process')
 protocol.registerSchemesAsPrivileged([{scheme:'cinema-dock',privileges:{standard:true,secure:true,supportFetchAPI:true,codeCache:true}}])
}

if(process.env.DANMU_ELECTRON_USER_DATA)app.setPath('userData',path.resolve(process.env.DANMU_ELECTRON_USER_DATA))

else if(app.isPackaged)app.setPath('userData',path.join(path.dirname(process.execPath),'data','electron'))

let win=null,tray=null,core=null,lastState=null,quitting=false,shutdown=null,geometry={},uiSession=null,closingWindow=null,restoreRequested=false
const configuredTrayCache=Number(process.env.DANMU_TRAY_CACHE_MS)
const trayCacheMs=process.env.DANMU_TRAY_CACHE_MS!==undefined&&Number.isFinite(configuredTrayCache)?Math.max(0,Math.min(60000,configuredTrayCache)):qa?0:20000
const trayCache=require('./tray-window-cache.cjs').createTrayWindowCache({duration:trayCacheMs,getCurrent:()=>win,release:target=>{closingWindow=target;target.webContents.send('window:state',{releasing:true});target.destroy()}})

let player=null
let primedGpuKey=''
const dialogPrimes=new WeakMap()
const gpuKey=()=>{try{const gpu=app.getAppMetrics().find(p=>p.type==='GPU');return gpu?`${gpu.pid}:${gpu.creationTime}`:''}catch{return ''}}

const initializingWindows=new WeakSet()
const rendererReadiness=new WeakMap()
const readyWindow=require('./window-ready.cjs')({isCurrent:target=>win===target,beforeCapture:target=>rendererReadiness.get(target)?.promise,onReady:target=>{primedGpuKey=gpuKey();target.emit('content-ready')}})

const loginArgs=app.isPackaged?['--tray','--start']:[base,'--tray','--start']

const loginItems=createLoginItems(app,{path:process.execPath,args:loginArgs})

const dataRoot=()=>app.isPackaged?path.dirname(process.execPath):path.join(base,'runtime-data')

const stateFile=()=>path.join(app.getPath('userData'),'window.json')

function persist(){try{fs.mkdirSync(app.getPath('userData'),{recursive:true});fs.writeFileSync(stateFile(),JSON.stringify(geometry))}catch{}}

function fitNativeBounds(window,target){let requested={x:target.x,y:target.y,width:target.width,height:target.height};for(let i=0;i<4;i++){window.setBounds(requested);const actual=window.getBounds();if(actual.width===target.width&&actual.height===target.height)break;requested={...requested,width:requested.width+target.width-actual.width,height:requested.height+target.height-actual.height}}}

const fixture=qa?require(process.env.DANMU_QA_FIXTURE||'../tests/fixture.cjs')():null

const allowed=new Set(['renameFiles','renamePreview','renameApply','cover','state','start','stop','scan','refresh','library','select','selectClear','prepare','pause','resume','cancel','retry','search','identify','episodes','plan','planSelect','downloadBatch','downloadSingle','refreshDanmu','history','historyAdd','historyRemove','historyClear','cache','cacheRemove','cacheClear','cacheRetention','logs','logsClear','logRetention','sources','autoMatch','preferences','initialize','addLibrary','libraries','removeLibrary','libraryType','connection','schedule','scheduleCancel','deletePlan','deleteConfirm','firewall'])

function trusted(event){if(!win||event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)throw new Error('不允许的调用来源')}

function remember(){if(!win||win.isDestroyed()||win.isMinimized())return;geometry={...win.getNormalBounds(),maximized:win.isMaximized()};persist()}

function show(){if(quitting)return;if(win&&!win.isDestroyed()){if(closingWindow===win){restoreRequested=true;return}if(initializingWindows.has(win))return;trayCache.resume(win);if(offscreenTest){win.webContents.invalidate();return}win.webContents.setBackgroundThrottling(false);if(win.isMinimized())win.restore();win.show();win.focus();return}

  restoreRequested=false

  const initialGeometry={...geometry}

  win=new BrowserWindow({width:geometry.width||1320,height:geometry.height||880,minWidth:940,minHeight:640,...(Number.isFinite(geometry.x)?{x:geometry.x,y:geometry.y}:{}),show:false,frame:false,backgroundColor:'#e6edf1',icon:path.join(base,'public/icon.ico'),roundedCorners:true,webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,backgroundThrottling:false,offscreen:offscreenTest}})

  const created=win
  let resolveRendererReady
  rendererReadiness.set(created,{promise:new Promise(resolve=>{resolveRendererReady=resolve}),resolve:()=>resolveRendererReady()})

  // A hidden Windows HWND can suppress compositor work despite renderer

  // timers being enabled. Prime this same window without focus or hit testing,

  // then reveal it atomically after the actual glass resources are resident.

  if(!offscreenTest){created.setOpacity(1/255);created.setIgnoreMouseEvents(true)}else created.webContents.setFrameRate(30)

  initializingWindows.add(created)

  let nativeReady=false,contentReady=false,presented=false

  const present=()=>{if(presented||!nativeReady||!contentReady||created.isDestroyed()||win!==created)return;presented=true;initializingWindows.delete(created);clearTimeout(readyFallback);created.setIgnoreMouseEvents(false);created.setOpacity(1);if(!qa&&!offscreenTest){created.show();created.focus()}}

  const readyFallback=setTimeout(()=>{contentReady=true;present()},10000);readyFallback.unref()

  created.once('content-ready',()=>{contentReady=true;present()})

  created.setMenu(null);created.loadFile(path.join(base,'dist/index.html'),qa?{hash:'qa'}:{})

  created.once('ready-to-show',()=>{if(win!==created||created.isDestroyed())return;fitNativeBounds(created,{...created.getBounds(),...initialGeometry,width:initialGeometry.width||1320,height:initialGeometry.height||880});if(initialGeometry.maximized&&!offscreenTest)created.maximize();nativeReady=true;if(!contentReady&&!offscreenTest)created.showInactive();present()})

  created.on('resize',()=>{if(win!==created)return;remember();created.webContents.send('window:state',{maximized:created.isMaximized()})});created.on('move',()=>{if(win===created)remember()})

  created.webContents.setWindowOpenHandler(()=>({action:'deny'}));created.webContents.on('will-navigate',(event,url)=>{if(url!==pathToFileURL(path.join(base,'dist/index.html')).href)event.preventDefault()})
  created.webContents.on('render-process-gone',()=>{if(trayCache.isParked(created)&&!created.isDestroyed()){trayCache.forget(created);created.destroy()}})

  created.on('close',event=>{if(win!==created)return;remember();if(quitting||qa&&trayCacheMs===0){closingWindow=created;return}if(lastState?.settings?.CloseToTray!==false){if(trayCache.park(created)){event.preventDefault();return}closingWindow=created;created.webContents.send('window:state',{releasing:true});return}event.preventDefault();quit()})

  const visibility=visible=>{

    if(!created.isDestroyed()){

      created.webContents.setBackgroundThrottling(!visible&&!trayCache.isParked(created))

      if(visible)created.webContents.invalidate()

    }

    if(visible?win!==created:win&&win!==created)return

    if(core&&!quitting)core.ready.then(()=>{

      if(quitting||core.child.exitCode!==null)return

      if(visible?win!==created||created.isDestroyed():win&&win!==created)return

      return core.call('visibility',{visible})

    }).catch(()=>{})

  }

  created.on('focus',()=>{if(!created.isDestroyed()&&created.isVisible()&&!created.isMinimized()){created.webContents.setBackgroundThrottling(false);created.webContents.invalidate()}})

  created.on('show',()=>visibility(true));created.on('hide',()=>visibility(false));created.on('minimize',()=>visibility(false));created.on('restore',()=>visibility(true))

  // A quickly restored window may exist before the old window finishes closing.

  // Only the owner may clear the foreground reference or publish its visibility.

  created.on('closed',()=>{trayCache.forget(created);clearTimeout(readyFallback);if(closingWindow===created)closingWindow=null;if(win===created){visibility(false);win=null}if(restoreRequested&&!quitting)show()})

}

const trace=phase=>{if(process.env.DANMU_LIFECYCLE_TRACE)fs.appendFileSync(process.env.DANMU_LIFECYCLE_TRACE,Date.now()+' '+phase+'\n')}

async function quit(confirmed=false){if(shutdown)return shutdown;if(!confirmed&&!qa&&(lastState?.batchRunning||lastState?.preparing||lastState?.loading)){show();const send=()=>win?.webContents.send('window:state',{requestExit:true});if(win.webContents.isLoading())win.webContents.once('did-finish-load',send);else send();return}quitting=true;trayCache.dispose();trace('quit-request');shutdown=(async()=>{try{await player?.stop();trace('core-stop');await core?.close();trace('core-stopped')}catch(error){trace('core-error:'+error.message)}tray?.destroy();win?.destroy();trace('app-quit');app.quit()})();return shutdown}

if(!qa&&!app.requestSingleInstanceLock()){app.quit()}else{

app.on('second-instance',show)

app.whenReady().then(async()=>{
  if(process.env.DANMU_ISOLATED_DOCK==='1')protocol.handle('cinema-dock',request=>{
   const url=new URL(request.url),root=path.join(base,'dist');let file
   try{file=path.resolve(root,'.'+decodeURIComponent(url.pathname))}catch{return new Response('Invalid asset',{status:400})}
   const relative=path.relative(root,file)
   if(url.host!=='dock'||request.method!=='GET'||!relative||relative.startsWith('..')||path.isAbsolute(relative))return new Response('Invalid asset',{status:403})
   return net.fetch(pathToFileURL(file).href)
  })

  nativeTheme.themeSource='light';try{geometry=JSON.parse(fs.readFileSync(stateFile(),'utf8'))}catch{}

  if(!qa){

    const engine=app.isPackaged?path.join(process.resourcesPath,'engine'):path.join(base,'development'),data=dataRoot()

    if(app.isPackaged){const target=path.join(data,'data/jellyfin/plugins');fs.mkdirSync(target,{recursive:true});const plugins=path.join(engine,'plugins');if(fs.existsSync(plugins))for(const name of fs.readdirSync(plugins)){const destination=path.join(target,name);if(!fs.existsSync(destination))fs.cpSync(path.join(plugins,name),destination,{recursive:true})}const scripts=path.join(data,'scripts');fs.mkdirSync(scripts,{recursive:true});fs.copyFileSync(path.join(engine,'scripts/configure-firewall.ps1'),path.join(scripts,'configure-firewall.ps1'))}

    core=new CoreBridge(path.join(engine,app.isPackaged?'bin/DanmuCinema.exe':'bin/installer-app/DanmuCinema.exe'),{DANMU_DATA_ROOT:data,DANMU_RUNTIME_ROOT:app.isPackaged?path.join(engine,'runtime/jellyfin'):path.join(base,'runtime/jellyfin'),DANMU_LAUNCHER:app.isPackaged?process.execPath:'',DANMU_START_SERVICES:process.argv.includes('--start')?'1':'0'})

    core.on('state',state=>{lastState=state;player?.setAutomaticMatching(state.settings?.AutoMatchDanmu!==false);win?.webContents.send('core:state',state);tray?.setToolTip(state.running?'弹幕影院 · 服务运行中':'弹幕影院 · 服务已停止')})

    core.on('foreground',show);core.on('offline',message=>{win?.webContents.send('core:state',{...lastState,offline:message});if(!quitting)dialog.showErrorBox('媒体核心停止',message)})

    core.ready.catch(error=>dialog.showErrorBox('启动失败',error.message))

  }

  player=new LocalPlayer({createOverlay:process.platform==='win32'?options=>new(require('./overlay-host.cjs').PlayerOverlay)({...options,base,packaged:app.isPackaged}):undefined,gpuAdapter:require('./gpu-adapter.cjs').adapterProvider(app),runtime:app.isPackaged?path.join(process.resourcesPath,'engine/runtime/mpv'):path.join(base,'runtime/mpv'),scripts:app.isPackaged?path.join(process.resourcesPath,'player'):path.join(base,'player'),storage:qa?path.join(app.getPath('userData'),'qa-player'):path.join(dataRoot(),'data/player'),prepare:async(file,token)=>{if(qa)return{ready:false};await core.ready;return core.call('playbackPrepare',{key:file.key,token})},cancelPrepare:token=>{if(!qa)core?.call('playbackCancel',{token}).catch(()=>{})}})

  player.setAutomaticMatching(lastState?.settings?.AutoMatchDanmu!==false);

  player.on('state',state=>win?.webContents.send('player:state',state));player.on('fault',message=>win?.webContents.send('player:fault',message));player.on('controls',()=>{show();const opened=win;const send=()=>opened?.webContents.send('window:state',{playerSettings:true});if(opened?.webContents.isLoading())opened.webContents.once('did-finish-load',send);else send()})

  // The isolated offscreen QA path must never add a tray icon or activate a
  // native window while the user's desktop is in use.
  tray=offscreenTest?Object.assign(new(require('node:events').EventEmitter)(),{setToolTip(){},setContextMenu(){},destroy(){}}):new Tray(path.join(base,'public/icon.ico'));tray.setToolTip('DanmuCinema · 弹幕影院');tray.on('click',show);tray.on('double-click',show)

  tray.setContextMenu(Menu.buildFromTemplate([{label:'打开弹幕影院',click:show},{label:'启动服务',click:()=>core?.call('start').catch(()=>{})},{label:'停止服务',click:()=>core?.call('stop').catch(()=>{})},{type:'separator'},{label:'取消定时',click:()=>core?.call('scheduleCancel')},{label:'退出并停止服务',click:()=>quit()}]))

  ipcMain.handle('core:call',async(event,method,params)=>{trusted(event);if(!allowed.has(method))throw new Error('不支持的操作');if(qa){const result=await fixture.call(method,params);lastState=fixture.snap();win?.webContents.send('core:state',lastState);return result;}await core.ready;return core.call(method,params)})

  ipcMain.handle('player:action',async(event,action,value)=>{trusted(event);if(action==='open'){if(qa)return{active:false,danmu:'演示数据不启动实际播放器'};await core.ready;const files=await core.call('playbackResolve',{keys:value?.keys,natural:!!value?.natural,siblings:!!value?.siblings});return player.play(files,value?.key||files[0]?.key)}return player.action(action,value)})

  ipcMain.handle('window:action',async(event,action)=>{trusted(event);if(action==='uiReady'){rendererReadiness.get(win)?.resolve();return}if(action==='primeDialog'){const target=win;let prime=dialogPrimes.get(target);if(!prime){prime=target.webContents.capturePage({x:0,y:0,width:1,height:1},{stayHidden:true,stayAwake:true}).finally(()=>dialogPrimes.delete(target));dialogPrimes.set(target,prime)}await prime;return}if(action==='prime'){const target=win;await target.webContents.capturePage(undefined,{stayHidden:true,stayAwake:true});return}if(action==='ready'){await readyWindow(win);return}else if(action==='minimize')win.minimize();else if(action==='maximize')win.isMaximized()?win.unmaximize():win.maximize();else if(action==='close')win.close();else if(action==='state')return{maximized:win.isMaximized()};else if(action==='scale100'||action==='scale120')win.webContents.setZoomFactor(action==='scale120'?1.2:1)})

  ipcMain.handle('system:action',async(event,action,value)=>{trusted(event)

    if(action==='chooseFolder'){const result=await dialog.showOpenDialog(win,{properties:['openDirectory']});return result.canceled?null:result.filePaths[0]}
    if(action==='chooseRenameFiles'){const video=value==='video',result=await dialog.showOpenDialog(win,{title:video?'选择视频文件':'选择弹幕或字幕文件',properties:['openFile','multiSelections'],filters:[video?{name:'视频文件',extensions:['mkv','mp4','m4v','avi','mov','wmv','webm','flv','ts','m2ts','mts','mpg','mpeg','vob','ogv','3gp','iso']}:{name:'弹幕与字幕',extensions:['xml','ass','ssa','srt','vtt','smi','sup','sub','idx','ttml']}]});return result.canceled?[]:result.filePaths}


    if(action==='copy'){clipboard.writeText(String(value));return true}

    if(action==='openFolder'){const target=lastState?.settings?.MediaFolder;if(!target)throw new Error('尚未设置媒体目录');return shell.openPath(target)}

    if(action==='openLogs')return shell.openPath(path.join(dataRoot(),'data'))

    if(action==='openWeb')return shell.openExternal(`http://127.0.0.1:${lastState?.settings?.Port||8096}/web/`)

    if(action==='feedback')return shell.openExternal('mailto:lizimu.hai@outlook.com')

    if(action==='autoStartGet')return loginItems.get()

    if(action==='autoStart')return loginItems.set(value)

    if(action==='quitConfirmed'){setImmediate(()=>quit(true));return true}

    if(action==='captureDock'){
      const zoom=event.sender.getZoomFactor(),width=Math.ceil(win.getContentBounds().width/zoom),height=Math.ceil(win.getContentBounds().height/zoom)
      if(!value||!['x','y','width','height'].every(k=>Number.isFinite(value[k]))||value.width<=0||value.width>6000||value.height<=0||value.height>300)throw Error('无效的导航绘制范围。')
      const x=Math.max(0,Math.floor(value.x*zoom)),y=Math.max(0,Math.floor(value.y*zoom)),w=Math.max(1,Math.min(Math.ceil(value.width*zoom),(width*zoom)-x)),h=Math.max(1,Math.min(Math.ceil(value.height*zoom),(height*zoom)-y)),rect={x,y,width:Math.floor(w),height:Math.floor(h)}
      const capture=await event.sender.capturePage(rect,{stayHidden:true,stayAwake:true});return{url:capture.toDataURL(),x:x/zoom,y:y/zoom,width:rect.width/zoom,height:rect.height/zoom}
    }
    if(action==='sessionGet')return{...uiSession,dockRimGpu:process.env.DANMU_EXPERIMENTAL_WEBGL_RIM==='1',dockUnifiedShader:process.env.DANMU_EXPERIMENTAL_DOCK_SHADER==='1',opticsPrimed:!!primedGpuKey&&primedGpuKey===gpuKey()}

    if(action==='sessionSet'){uiSession={...uiSession,...value};return true}

  })

  if(!process.argv.includes('--tray'))show()

  if(qa)show()

  if(qa){const script=process.env.DANMU_QA_SCRIPT;if(script)require(script)({app,getWindow:()=>win,getPlayer:()=>player,getTray:()=>tray,show,quit})}

})

app.on('window-all-closed',()=>{});app.on('before-quit',event=>{if(!quitting){event.preventDefault();quit()}})

}
