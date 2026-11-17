'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{execFile}=require('node:child_process'),run=require('node:util').promisify(execFile)
const {LocalPlayer}=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs')
const out=path.resolve('artifacts/overlay-matrix-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,matrix:[],speed:[],checks:[]}
const logCase=item=>{if(process.env.DANMU_TEST_QUIET!=='1')console.log(JSON.stringify(item))}
const packagedRoot=process.env.DANMU_PACKAGED_ROOT
const player=new LocalPlayer({runtime:path.resolve(packagedRoot?path.join(packagedRoot,'resources/engine/runtime/mpv'):'runtime/mpv'),scripts:path.resolve(process.env.DANMU_TEST_SCRIPTS||(packagedRoot?path.join(packagedRoot,'resources/player'):'player')),storage:path.join(out,'state'),createOverlay:options=>new PlayerOverlay({...options,base:packagedRoot?path.join(packagedRoot,'resources/app.asar'):process.cwd(),packaged:!!packagedRoot,executable:packagedRoot?path.join(packagedRoot,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe')}),extraArgs:['--volume=0',...(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)]})
const cmd=(...args)=>player.ipc.command(args),get=name=>cmd('get_property',name)
const quantile=(a,p)=>[...a].sort((x,y)=>x-y)[Math.min(a.length-1,Math.floor(a.length*p))],summary=a=>({n:a.length,p50:quantile(a,.5),p95:quantile(a,.95),p99:quantile(a,.99),max:Math.max(...a)}),intervals=a=>a.slice(1).map((t,i)=>t-a[i])
// "focused" calls VOCTRL_GET_FOCUSED synchronously. Keep window-state queries
// out of measured high-rate playback; they can serialize on the output thread.
async function sample(){return Object.fromEntries(await Promise.all(['frame-drop-count','decoder-frame-drop-count','mistimed-frame-count','vo-delayed-frame-count','display-sync-active','speed','avsync','time-pos'].map(async n=>{try{return[n,await get(n)]}catch(error){if(n==='mistimed-frame-count'&&/property unavailable/.test(error.message))return[n,null];throw Error(n+': '+error.message)}})))}
const counterDelta=(before,after,name)=>before[name]===null||after[name]===null?null:after[name]-before[name]
// mpv exposes mistimed-frame-count only while display sync is active. Preserve
// that as null, never a fake zero, and require audio-sync at both boundaries.
const validMistimed=(value,before,after)=>value===0||value===null&&before['display-sync-active']===false&&after['display-sync-active']===false
async function main(){await fs.mkdir(out,{recursive:true});const helper=path.join(out,'Window.exe')
 await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+helper,path.resolve('development/tests/MpvWindowProbe.cs')],{windowsHide:true})
 try{
  await player.configure({danmuFps:0,resume:false,fontSize:28,region:.3,duration:19,volume:0})
  await player.play([{key:'test',path:path.resolve(process.env.DANMU_TEST_MEDIA||'artifacts/real-movie-sample.mkv'),title:'播放组合验收'}],'test')
  for(let n=0;!player.state.danmuCount&&n<1000;n++){if(player.state.error)throw Error(player.state.error);await sleep(20)}
  if(!player.state.danmuCount)throw Error('Danmu did not load')
  if(process.env.DANMU_PLAYER_MANUAL_GATE==='1'){await cmd('set_property','pause',true);await fs.writeFile(path.join(out,'ready-for-foreground.json'),JSON.stringify({out,pid:player.child.pid}));let released=false;for(let i=0;i<6000;i++){if(await fs.access(path.join(out,'continue-foreground')).then(()=>true,()=>false)){released=true;break}await sleep(100)}if(!released)throw Error('foreground test activation timed out');await cmd('set_property','pause',false)}
  const rate=report.hz=await get('display-fps'),pid=player.child.pid;report.initial={...await sample(),focused:await get('focused')}
  if(process.env.DANMU_TRACE_SPEED==='1'){report.transitions=[];const at=performance.now();await cmd('observe_property',900,'avsync');player.ipc.on('event',e=>{if(e.event==='property-change'&&['speed','frame-drop-count','avsync'].includes(e.name))report.transitions.push({ms:performance.now()-at,name:e.name,value:e.data})})}
  const modes=(process.env.DANMU_TEST_MODES||'fullscreen,window,maximized').split(',')
  for(const mode of modes){
   await cmd('set_property','fullscreen',false);await run(helper,[mode==='maximized'?'maximize':'restore',String(pid)],{windowsHide:true});if(mode==='fullscreen')await cmd('set_property','fullscreen',true);else if(mode==='window')await run(helper,['resize',String(pid),'1160','720'],{windowsHide:true})
   await cmd('script-message-to','modernz','osc-visibility','never');await cmd('seek',20,'absolute+exact');await sleep(1000)
   for(const fps of (process.env.DANMU_TEST_RATES??'0,120,60').split(',').filter(Boolean).map(Number)){
    await player.configure({danmuFps:fps});await sleep(500);const before=await sample();await player.overlay.probe(true);const start=performance.now();await sleep(Number(process.env.DANMU_SAMPLE_MS||2500));const stats=await player.overlay.probe(),elapsed=performance.now()-start,after=await sample(),dt=intervals(stats.times),expected=Math.min(fps||rate,rate),budget=1500/expected
    const visible=await player.overlay.diagnose(),item={mode,fps,actualFps:stats.draws*1000/elapsed,submittedFps:stats.gpu?.submitted*1000/elapsed,frame:{...summary(dt),max:stats.frameMax??Math.max(...dt),totalIntervals:stats.frameIntervals},raf:summary(intervals(stats.rafTimes)),cost:summary(stats.costs),missed:stats.frameMisses??dt.filter(t=>t>budget).length,drift:stats.drift,clockBacksteps:stats.clockBacksteps,maxClockBackstep:stats.maxClockBackstep,maxClockStepError:stats.maxClockStepError,visible,videoDrops:after['frame-drop-count']-before['frame-drop-count'],mistimed:counterDelta(before,after,'mistimed-frame-count'),displaySyncBefore:before['display-sync-active'],displaySyncAfter:after['display-sync-active'],voDelayed:after['vo-delayed-frame-count']-before['vo-delayed-frame-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],errors:stats.errors,gpu:stats.gpu}
    item.passed=(!stats.gpu||stats.gpu.skipped===0&&stats.gpu.failures===0&&(!stats.gpu.submitted||Math.abs(item.submittedFps-expected)<expected*.05))&&item.missed===0&&item.clockBacksteps===0&&item.videoDrops===0&&item.decoderDrops===0&&validMistimed(item.mistimed,before,after)&&item.voDelayed===0&&Math.abs(item.actualFps-expected)<expected*.05&&Math.abs(item.drift)<.05&&visible.alphaPixels>1000&&visible.compositedPixels>1000&&visible.visible&&stats.errors.length===0
    report.matrix.push(item);logCase(item)
   }
   await player.configure({danmuFps:0})
   for(const speed of (process.env.DANMU_TEST_SPEEDS??'2,3,4,5,6').split(',').filter(Boolean).map(Number)){
    await player.configure({holdSpeed:speed});await cmd('seek',20,'absolute+exact');await sleep(900)
    for(let repeat=0;repeat<Number(process.env.DANMU_TEST_REPEATS||2);repeat++){
     const key=repeat%2?'LEFT':'RIGHT',before=await sample();await player.overlay.probe(true);await cmd('keydown',key);await sleep(1200);const held=await sample();await cmd('keyup',key);await sleep(1200);const after=await sample(),stats=await player.overlay.probe(),dt=intervals(stats.times),item={mode,speed,key,repeat,entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count'],mistimed:counterDelta(before,after,'mistimed-frame-count'),displaySyncBefore:before['display-sync-active'],displaySyncAfter:after['display-sync-active'],voDelayed:after['vo-delayed-frame-count']-before['vo-delayed-frame-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],heldSpeed:held.speed,returnedSpeed:after.speed,avsync:after.avsync,clockBacksteps:stats.clockBacksteps,danmuMisses:stats.frameMisses??dt.filter(t=>t>1500/rate).length,danmuFrameMax:stats.frameMax??Math.max(...dt),gpu:stats.gpu}
     item.passed=(!stats.gpu||stats.gpu.skipped===0&&stats.gpu.failures===0)&&item.entryDrops===0&&item.exitDrops===0&&item.decoderDrops===0&&validMistimed(item.mistimed,before,after)&&item.voDelayed===0&&item.clockBacksteps===0&&item.danmuMisses===0&&item.heldSpeed===speed&&item.returnedSpeed===1&&Math.abs(item.avsync)<.05;report.speed.push(item);logCase(item)
    }
   }
  }
  report.final=await sample()
  // A lower audio buffer can conceal video drops by repeatedly starving AO.
  // Never certify that as a smooth playback result.
  const log=await fs.readFile(path.join(out,'state','player.log'),'utf8')
  report.audioUnderruns=(log.match(/Audio device underrun detected\./g)||[]).length
  report.avSyncWarnings=(log.match(/Audio\/Video desynchronisation detected/g)||[]).length
  report.passed=report.matrix.length+report.speed.length>0&&report.matrix.every(c=>c.passed)&&report.speed.every(c=>c.passed)&&report.audioUnderruns===0&&report.avSyncWarnings===0
 }catch(error){report.error=error.stack;process.exitCode=1}finally{await player.stop();if(!report.passed)process.exitCode=1;await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed,error:report.error}))}
}
main().catch(error=>{console.error(error);process.exitCode=1})
