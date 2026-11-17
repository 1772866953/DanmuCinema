'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{LocalPlayer}=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs')
const out=path.resolve('artifacts/integrated-overlay-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,cases:[]}
const player=new LocalPlayer({runtime:path.resolve('runtime/mpv'),scripts:path.resolve(process.env.DANMU_TEST_SCRIPTS||'player'),storage:path.join(out,'state'),createOverlay:options=>new PlayerOverlay({...options,base:process.cwd(),executable:path.resolve('tools/electron/electron.exe')}),extraArgs:['--volume=0',...(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)]})
const cmd=(...args)=>player.ipc.command(args)
async function sample(){const names=['frame-drop-count','decoder-frame-drop-count','speed','avsync'];return Object.fromEntries(await Promise.all(names.map(async n=>[n,await cmd('get_property',n)])))}
async function main(){await fs.mkdir(out,{recursive:true});try{
 await player.configure({danmuFps:0,resume:false,fontSize:28,region:.3,duration:19,volume:0});const began=performance.now()
 await player.play([{key:'test',path:path.resolve('artifacts/real-movie-sample.mkv'),title:'集成播放检查'}],'test');report.openMs=performance.now()-began
 for(let n=0;!player.state.danmuCount&&n<1000;n++){if(player.state.error)throw Error(player.state.error);await sleep(20)}
 if(!player.state.danmuCount)throw Error('Danmu load timeout')
 if(process.env.DANMU_TRACE_SPEED==='1'){report.transitions=[];const began=performance.now();await cmd('observe_property',900,'avsync');player.ipc.on('event',e=>{if(e.event==='property-change'&&['speed','frame-drop-count','avsync'].includes(e.name))report.transitions.push({ms:performance.now()-began,name:e.name,value:e.data})})}
 await cmd('seek',20,'absolute+exact');await sleep(1200);report.visibility=await player.overlay.diagnose();if(report.visibility.alphaPixels<1000)throw Error('No visible danmu glyphs')
 const first=await player.overlay.probe();await sleep(2000);const second=await player.overlay.probe();report.clock={...second,actualFps:(second.draws-first.draws)/2};delete report.clock.times;delete report.clock.rafTimes;delete report.clock.costs
 for(const speed of [2,6]){
  await player.configure({holdSpeed:speed});await cmd('seek',20,'absolute+exact');await sleep(800)
  for(let repeat=0;repeat<2;repeat++){
   const before=await sample();await cmd('keydown','RIGHT');await sleep(2500);const held=await sample();await cmd('keyup','RIGHT');await sleep(1400);const after=await sample()
   const item={speed,repeat,entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],heldSpeed:held.speed,returnedSpeed:after.speed,avsync:after.avsync};report.cases.push(item);console.log(JSON.stringify(item))
  }
 }
 const before=await sample();await cmd('script-binding','cinema/cinema-settings');await sleep(400);await cmd('keypress','ESC');await sleep(400);const after=await sample();report.settingsDrops=after['frame-drop-count']-before['frame-drop-count']
 report.passed=report.clock.actualFps>190&&Math.abs(report.clock.drift)<.05&&report.settingsDrops===0&&report.cases.every(c=>c.entryDrops===0&&c.exitDrops===0&&c.decoderDrops===0&&c.heldSpeed===c.speed&&c.returnedSpeed===1&&Math.abs(c.avsync)<.05)
 }catch(error){report.error=error.stack;process.exitCode=1}finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report,transitions:report.transitions?.length},null,2))}}
main().catch(error=>{console.error(error);process.exitCode=1})
