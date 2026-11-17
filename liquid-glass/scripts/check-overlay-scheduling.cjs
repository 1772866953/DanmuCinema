'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{LocalPlayer}=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs')
const out=path.resolve('artifacts/overlay-scheduling-'+Date.now()),sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms)),report={phases:[],passed:false}
const player=new LocalPlayer({runtime:path.resolve('runtime/mpv'),scripts:path.resolve('player'),storage:path.join(out,'state'),extraArgs:['--volume=0','--fullscreen=no','--geometry=1160x720+30+30'],createOverlay:options=>new PlayerOverlay({...options,base:process.cwd(),executable:path.resolve('tools/electron/electron.exe')})})
const cmd=(...args)=>player.ipc.command(args)
async function main(){await fs.mkdir(out,{recursive:true});try{
 await player.configure({resume:false,volume:0,danmuFps:0});await player.play([{key:'test',path:path.resolve('artifacts/overlay-4k-fixture/test.mkv'),title:'弹幕调度追踪'}],'test')
 for(let count=0;!player.state.danmuCount&&count<1000;count++)await sleep(20)
 if(!player.state.danmuCount)throw Error('danmu not loaded')
 await cmd('script-message-to','modernz','osc-visibility','never');await sleep(1200)
 if(process.env.DANMU_OVERLAY_QA_TRACE==='1')await player.overlay.request('qa-trace-start')
 for(let index=0;index<Number(process.env.DANMU_SCHEDULING_PHASES||4);index++){
  const before=await cmd('get_property','frame-drop-count');await player.overlay.probe(true);await sleep(Number(process.env.DANMU_SCHEDULING_MS||15000));const stats=await player.overlay.probe(),after=await cmd('get_property','frame-drop-count')
  const item={index,videoDrops:after-before,misses:stats.frameMisses,frameMax:stats.frameMax,frames:stats.draws,gpu:stats.gpu,lateFrames:stats.lateFrames,clockBacksteps:stats.clockBacksteps,drift:stats.drift};report.phases.push(item);console.log(JSON.stringify(item))
 }
 if(process.env.DANMU_OVERLAY_QA_TRACE==='1'){
  report.trace=await player.overlay.request('qa-trace-stop',null,30000);const events=JSON.parse(await fs.readFile(report.trace)).traceEvents
  report.lateEvents=events.filter(e=>e.name==='TimeStamp'&&e.args?.data?.message?.startsWith('danmu-late:')).map(marker=>({marker,nearby:events.filter(e=>e.pid===marker.pid&&e.tid===marker.tid&&e.ts>=marker.ts-30000&&e.ts<=marker.ts&&e.dur>=1000).map(e=>({name:e.name,ms:e.dur/1000,atMs:(e.ts-marker.ts)/1000,args:e.args}))}))
 }
 report.passed=report.phases.every(p=>p.videoDrops===0&&p.misses===0&&p.clockBacksteps===0&&p.gpu.skipped===0)
}catch(error){report.error=error.stack;process.exitCode=1}finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed,error:report.error}));if(!report.passed)process.exitCode=1}}
main().catch(error=>{console.error(error);process.exitCode=1})
