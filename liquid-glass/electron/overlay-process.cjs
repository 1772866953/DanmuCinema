'use strict'
const {app,BrowserWindow,protocol,ipcMain,screen}=require('electron'),fs=require('node:fs/promises'),path=require('node:path'),net=require('node:net'),readline=require('node:readline')
const {MpvIpc}=require('./mpv-ipc.cjs')
const config=JSON.parse(process.env.DANMU_OVERLAY_CONFIG);delete process.env.DANMU_OVERLAY_CONFIG
app.setPath('userData',path.join(config.storage,'overlay-profile'))
if(process.env.DANMU_OVERLAY_GRAPHITE!=='0')app.commandLine.appendSwitch('enable-features','SkiaGraphite')
for(const flag of ['disable-renderer-backgrounding','disable-backgrounding-occluded-windows','force_high_performance_gpu'])app.commandLine.appendSwitch(flag)
app.commandLine.appendSwitch('disable-features',[...new Set([...app.commandLine.getSwitchValue('disable-features').split(',').filter(Boolean),'CalculateNativeWinOcclusion','UseEcoQoSForBackgroundProcess'])].join(','))
protocol.registerSchemesAsPrivileged([{scheme:'cinema-overlay',privileges:{standard:true,secure:true,supportFetchAPI:true,corsEnabled:true}}])
let window,ipc,sinkName,ending=false,position=0,speed=1,paused=false,seeking=false,buffering=false,minimized=false,audioDelay=0,hasAudio=false,epoch=0,loaded=false
const transport=net.createConnection(config.transport)
const connected=new Promise((resolve,reject)=>{transport.once('connect',()=>{transport.write(JSON.stringify({token:config.token})+'\n');resolve()});transport.once('error',reject)})
connected.catch(()=>{})
const send=value=>{if(!transport.destroyed)transport.write(JSON.stringify(value)+'\n')},sleep=ms=>new Promise(r=>setTimeout(r,ms))
const clock=reset=>{if(window&&!window.isDestroyed())window.webContents.send('overlay-clock',{time:position+audioDelay,speed,paused:paused||seeking||buffering||minimized,reset})}
const command=(...args)=>ipc.command(args)
async function event(e){
 if(!window||window.isDestroyed()||ending)return
 if(e.event==='client-message'&&e.args?.[0]==='cinema-overlay'){window.webContents.send('overlay-ui',JSON.parse(e.args[1]));return}
 if(e.event==='start-file'){hasAudio=false;position=0;seeking=true;epoch++;clock(true);await window.webContents.executeJavaScript('window.assOverlay.clear()');return}
 if(e.event==='seek'){seeking=true;epoch++;clock(true);return}
 if(e.event==='playback-restart'){const generation=++epoch,value=await command('get_property','time-pos').catch(()=>null);if(generation===epoch&&Number.isFinite(value)){position=value;seeking=false;clock(true)}return}
 if(e.event==='file-loaded'){loaded=true;await command('set_property','user-data/cinema-overlay-visible',true);window.showInactive();return}
 if(e.event!=='property-change')return
 const {name,data}=e
 if(name==='audio-pts'&&Number.isFinite(data)){hasAudio=true;position=data;clock(false)}
 else if(name==='time-pos'&&!hasAudio&&Number.isFinite(data)){position=data;clock(false)}
 else if(name==='speed'){speed=data||1;clock(false)}
 else if(name==='pause'){paused=!!data;clock(true)}
 else if(name==='paused-for-cache'){buffering=!!data;clock(true)}
 else if(name==='audio-delay'){audioDelay=data||0;clock(true)}
 else if(name==='window-minimized'){minimized=!!data;clock(true)}
 else if(name==='osd-dimensions'&&data)window.webContents.send('overlay-geometry',data)
 else if(name==='user-data/cinema-host'){
  if(loaded&&data?.visible&&!window.isVisible())window.showInactive()
  else if(data?.visible===false&&window.isVisible())window.hide()
 }
}
async function attach(pipe){
 ipc=new MpvIpc();ipc.on('event',e=>{void event(e).catch(error=>console.error(error.message))});ipc.on('disconnect',()=>{if(!ending){send({event:'mpv-closed'});window?.hide();void shutdown()}})
 await ipc.connect(pipe)
 sinkName=await command('client_name');await command('script-message','cinema-overlay-sink',sinkName)
 for(const [i,name]of ['audio-pts','time-pos','speed','pause','paused-for-cache','audio-delay','window-minimized','osd-dimensions','user-data/cinema-host'].entries())await command('observe_property',1000+i,name)
 return true
}
async function prepareControls(){
 await command('script-message','cinema-overlay-sink',sinkName)
 await window.webContents.executeJavaScript('window.assOverlay.warmUI()')
 if(process.env.DANMU_OVERLAY_PRIME_HINTS==='1'){
  const dim=await command('get_property','osd-dimensions'),hidpi=await command('get_property','display-hidpi-scale'),factor=(config.settings.uiFontSize||16)/16,height=dim.h/(Math.max(1,hidpi)*factor)
  await window.webContents.executeJavaScript('window.assOverlay.primeHints('+JSON.stringify({width:dim.w/dim.h*height,height})+')')
 }
 return true
}
async function shutdown(){
 if(ending)return;ending=true
 if(ipc&&!ipc.closed)await command('script-message-to','cinema_host','cinema-host-detach').catch(()=>{})
 ipc?.close()
 if(window&&!window.isDestroyed()){await window.webContents.executeJavaScript('window.assOverlay?.destroy()').catch(()=>{});window.destroy()}
 transport.end();setImmediate(()=>app.quit())
}
const started=app.whenReady().then(async()=>{
 await protocol.handle('cinema-overlay',async request=>{
  try{
   const relative=decodeURIComponent(new URL(request.url).pathname).replace(/^\/+/,''),fonts=relative.startsWith('runtime/mpv/fonts/'),root=fonts?path.join(config.runtime,'fonts'):path.join(config.base,'dist'),file=path.resolve(root,fonts?relative.slice('runtime/mpv/fonts/'.length):relative)
   if(!file.startsWith(root+path.sep))throw Error('invalid asset path')
   const mime={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css','.wasm':'application/wasm','.ttf':'font/ttf','.woff2':'font/woff2'}
   return new Response(await fs.readFile(file),{headers:{'Content-Type':mime[path.extname(file)]||'application/octet-stream','Cross-Origin-Embedder-Policy':'require-corp','Cross-Origin-Opener-Policy':'same-origin','Cross-Origin-Resource-Policy':'same-origin'}})
  }catch(error){console.error('overlay asset',request.url,error.stack);return new Response('Not found',{status:404})}
 })
 window=new BrowserWindow({width:900,height:600,show:false,transparent:true,frame:false,focusable:false,skipTaskbar:true,webPreferences:{preload:path.join(__dirname,'overlay-preload.cjs'),contextIsolation:true,sandbox:true,nodeIntegration:false,backgroundThrottling:false}})
 let displayHz=0
 const refreshDisplay=()=>{if(window.isDestroyed())return;const hz=screen.getDisplayMatching(window.getBounds()).displayFrequency||60;if(hz!==displayHz){displayHz=hz;window.webContents.send('overlay-display',hz)}}
 window.on('move',refreshDisplay);window.on('resize',refreshDisplay);screen.on('display-metrics-changed',refreshDisplay);refreshDisplay()
 window.setIgnoreMouseEvents(true);window.webContents.setWindowOpenHandler(()=>({action:'deny'}));window.webContents.on('will-navigate',(e,url)=>{if(!url.startsWith('cinema-overlay://app/'))e.preventDefault()})
 ipcMain.handle('overlay-bootstrap',e=>{if(e.sender!==window.webContents)throw Error('invalid renderer');return {...config.settings,displayHz}})
 const ready=new Promise((resolve,reject)=>{ipcMain.on('overlay-ready',e=>{if(e.sender===window.webContents)resolve()});window.webContents.once('render-process-gone',(_,details)=>reject(Error('overlay renderer stopped: '+details.reason)))})
 window.webContents.on('render-process-gone',(_,details)=>{if(!ending){console.error('overlay renderer stopped: '+details.reason);process.exitCode=1;void shutdown()}})
 window.webContents.on('console-message',details=>console.error('overlay renderer:',details.message))
 window.webContents.on('did-fail-load',(_,code,description,url)=>console.error('overlay load',code,description,url))
 await connected;await window.loadURL('cinema-overlay://app/player-overlay/index.html');await ready
 send({event:'ready',hwnd:window.getNativeWindowHandle().readBigUInt64LE().toString(),pid:process.pid})
})
const input=readline.createInterface({input:transport,crlfDelay:Infinity})
input.on('line',line=>{void(async()=>{
 let message;try{
  message=JSON.parse(line);await started;let value
  if(message.method==='attach')value=await attach(message.value.pipe)
  else if(message.method==='prepare')value=await prepareControls()
  else if(message.method==='settings'){config.settings=message.value;value=await window.webContents.executeJavaScript('window.assOverlay.configure('+JSON.stringify(message.value)+')')}
  else if(message.method==='scene'){
   const file=path.resolve(message.value.file);if(path.dirname(file)!==path.resolve(config.storage))throw Error('invalid scene path')
   const stat=await fs.stat(file);if(stat.size>40*1024*1024)throw Error('scene too large')
   const scene=JSON.parse(await fs.readFile(file,'utf8'));value=await window.webContents.executeJavaScript('window.assOverlay.load('+JSON.stringify(scene)+')')
  }else if(message.method==='clear')value=await window.webContents.executeJavaScript('window.assOverlay.clear()')
  else if(message.method==='probe')value=await window.webContents.executeJavaScript('Promise.all([window.assOverlay.stats('+Boolean(message.value?.reset)+'),window.assOverlay.uiStats('+Boolean(message.value?.reset)+')]).then(([clock,ui])=>({...clock,ui}))')
  else if(message.method==='capture'){
   const image=await window.webContents.capturePage(undefined,{stayHidden:true,stayAwake:true}),file=path.join(config.storage,'overlay-capture.png');await fs.writeFile(file,image.toPNG());value=file
  }
  else if(message.method==='diagnose'){
   value=await window.webContents.executeJavaScript('window.assOverlay.diagnose()')
   value.skiaGraphite=app.getGPUFeatureStatus().skia_graphite
   value.windowMetrics={zoom:window.webContents.getZoomFactor(),bounds:window.getBounds(),content:window.getContentBounds(),displayScale:screen.getDisplayMatching(window.getBounds()).scaleFactor}
   const capture=await window.webContents.capturePage(undefined,{stayHidden:true,stayAwake:true}),pixels=capture.toBitmap();let compositedPixels=0
   for(let i=3;i<pixels.length;i+=4)if(pixels[i]>20)compositedPixels++
   value={...value,compositedPixels,captureSize:capture.getSize(),visible:window.isVisible()}
  }
  else if(message.method==='qa-trace-start'&&process.env.DANMU_OVERLAY_QA_TRACE==='1'){
   if(!window.webContents.debugger.isAttached())window.webContents.debugger.attach('1.3')
   await window.webContents.debugger.sendCommand('Tracing.start',{categories:'devtools.timeline,v8,disabled-by-default-v8.gc,renderer.scheduler',transferMode:'ReturnAsStream'});value=true
  }
  else if(message.method==='qa-trace-stop'&&process.env.DANMU_OVERLAY_QA_TRACE==='1'){
   const debug=window.webContents.debugger,done=new Promise(resolve=>{const listener=(_event,name,data)=>{if(name==='Tracing.tracingComplete'){debug.removeListener('message',listener);resolve(data)}};debug.on('message',listener)})
   await debug.sendCommand('Tracing.end');const {stream}=await done;let trace=''
   for(;;){const part=await debug.sendCommand('IO.read',{handle:stream});trace+=part.base64Encoded?Buffer.from(part.data,'base64').toString():part.data;if(part.eof)break}await debug.sendCommand('IO.close',{handle:stream});value=path.join(config.storage,'qa-scheduling-trace.json');await fs.writeFile(value,trace);debug.detach()
  }
  else if(message.method==='quit'){send({id:message.id,value:true});await shutdown();return}
  else throw Error('unsupported overlay operation')
  send({id:message.id,value:value??true})
 }catch(error){send({id:message?.id,error:error.message})}
 })()})
input.on('close',()=>{void shutdown()});app.on('window-all-closed',()=>{});app.on('before-quit',e=>{if(!ending){e.preventDefault();void shutdown()}})
started.catch(error=>{console.error(error.stack);process.exitCode=1;void shutdown()})
