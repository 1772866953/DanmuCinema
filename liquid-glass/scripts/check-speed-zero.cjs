'use strict'
require('./foreground-test-guard.cjs')()
// Transport checks must not repeatedly query static VO/color structures while
// measuring playback: those requests themselves serialize on the playback core.
const fs=require('node:fs/promises'),path=require('node:path'),{LocalPlayer}=require('../electron/player.cjs')
const out=path.resolve('artifacts/speed-zero-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,cases:[]}
const player=new LocalPlayer({runtime:path.resolve('runtime/mpv'),scripts:path.resolve(process.env.DANMU_TEST_SCRIPTS||'player'),storage:path.join(out,'state'),gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,extraArgs:['--volume=0','--script-opts-append=cinema-qa=yes','--script-opts-append=modernz-minmousemove=100000',...(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)]})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
async function snapshot(){const names=['frame-drop-count','decoder-frame-drop-count','vo-delayed-frame-count','avsync','speed','time-pos','display-fps'];return Object.fromEntries(await Promise.all(names.map(async n=>[n,await get(n)])))}
async function main(){await fs.mkdir(out,{recursive:true});try{
 await player.configure({danmuFps:0,resume:false,fontSize:21,region:.15});await player.play([{key:'test',path:path.resolve(process.env.DANMU_TEST_MEDIA||'artifacts/speed-transition-media.mkv'),title:'无干扰倍速零丢帧检查'}],'test');for(let i=0;!player.state.danmuCount&&i<300;i++)await sleep(20)
 if(process.env.DANMU_TEST_WINDOW==='1')await cmd('set_property','fullscreen',false)
 report.static={display:await get('display-fps'),fps:await get('container-fps'),video:await get('video-params'),target:await get('video-target-params'),hwdec:await get('hwdec-current')}
 if(process.env.DANMU_TRACE_SPEED==='1')for(const [i,name]of ['speed','video-sync','autosync','frame-drop-count','avsync'].entries())await cmd('observe_property',800+i,name)
 for(const speed of (process.env.DANMU_TEST_SPEEDS||'2,3').split(',').map(Number)){
  await player.configure({holdSpeed:speed});await cmd('set_property','time-pos',12);await cmd('script-message-to','modernz','osc-visibility','never');await sleep(1500)
  for(let repeat=0;repeat<Number(process.env.DANMU_TEST_REPEATS||4);repeat++){
   const events=[],transitionEvents=[],began=performance.now(),onEvent=e=>{if(['seek','playback-restart'].includes(e.event))events.push(e.event);if(process.env.DANMU_TRACE_SPEED==='1'&&e.event==='property-change'&&e.id>=800)transitionEvents.push({ms:performance.now()-began,name:e.name,value:e.data})};player.ipc.on('event',onEvent)
   const before=await snapshot();await cmd('keydown','RIGHT');await sleep(Number(process.env.DANMU_HOLD_MS||2500));const held=await snapshot();await cmd('keyup','RIGHT');await sleep(1400);const after=await snapshot();player.ipc.off('event',onEvent)
   const result={speed,repeat,entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count'],drops:after['frame-drop-count']-before['frame-drop-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],delayed:after['vo-delayed-frame-count']-before['vo-delayed-frame-count'],avsync:after.avsync,heldSpeed:held.speed,returnedSpeed:after.speed,events,before,held,after,...(transitionEvents.length?{transitionEvents}:{})};report.cases.push(result);console.log(JSON.stringify({...result,before:undefined,held:undefined,after:undefined,transitionEvents:undefined}))
  }
 }
 report.passed=report.cases.every(c=>c.drops===0&&c.decoderDrops===0&&c.heldSpeed===c.speed&&c.returnedSpeed===1&&Math.abs(c.avsync)<.05&&!c.events.length)
 }finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed}))}}
main().catch(e=>{console.error(e);process.exitCode=1})
