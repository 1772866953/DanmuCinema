'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict')
const packaged=process.env.DANMU_PACKAGED_ROOT,{LocalPlayer}=require(packaged?path.join(packaged,'resources/app.asar/electron/player.cjs'):'../electron/player.cjs')
const out=path.resolve('artifacts/speed-input-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,checks:[]}
const player=new LocalPlayer({runtime:packaged?path.join(packaged,'resources/engine/runtime/mpv'):path.resolve('runtime/mpv'),scripts:packaged?path.join(packaged,'resources/player'):path.resolve('player'),storage:path.join(out,'state'),gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,extraArgs:['--volume=0','--script-opts-append=cinema-qa=yes']})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n),check=(v,s)=>{assert(v,s);report.checks.push(s)}
async function wait(predicate){for(let i=0;i<100;i++){if(await predicate())return;await sleep(10)}throw Error('timed out')}
async function main(){await fs.mkdir(out,{recursive:true});try{
 await player.configure({holdSpeed:2,resume:false});await player.play([{key:'test',path:path.resolve('artifacts/speed-transition-24fps.mkv'),title:'输入与缓存预算测试'}],'test');await wait(()=>player.current);await sleep(500)
 await cmd('set_property','speed',1.25);await sleep(80)
 const events=[];const listener=e=>{if(e.event==='seek')events.push(e)};player.ipc.on('event',listener)
 await cmd('keydown','RIGHT');await sleep(650);check(await get('speed')===2,'hold reaches its target speed');await cmd('keyup','RIGHT');await wait(async()=>await get('speed')===1.25);check(events.length===0,'speed changes never seek or reset transport counters')
 await cmd('keydown','RIGHT');await sleep(320);await cmd('keyup','RIGHT');await cmd('keydown','LEFT');await sleep(650);await cmd('keyup','LEFT');await wait(async()=>await get('speed')===1.25);check(true,'rapid re-press during the speed ramp restores the original base speed')
 await cmd('keydown','RIGHT');await sleep(650);await cmd('script-binding','cinema/cinema-settings');await wait(async()=>await get('speed')===1.25);await cmd('keyup','RIGHT');await cmd('keypress','ESC');check(true,'opening settings during a hold cancels its temporary speed')
 await cmd('set_property','pause',true);await cmd('keydown','RIGHT');await sleep(380);check(await get('speed')===2,'a paused player does not add a needless speed animation delay');await cmd('keyup','RIGHT');await wait(async()=>await get('speed')===1.25);check(await get('pause')===true,'releasing a held key preserves pause state')
 player.ipc.off('event',listener)
 for(const budget of [0,100,25]){
  await player.configure({preloadBudget:budget});const [forward,backward]=require('../electron/player-config.cjs').preloadArgs({preloadBudget:budget}).map(a=>Number(a.match(/=(\d+)MiB/)[1])*1024*1024)
  check(await get('demuxer-max-bytes')===forward&&await get('demuxer-max-back-bytes')===backward,`budget ${budget} changes actual mpv cache limits`)
 }
 await sleep(750);check(await get('autosync')===0,'temporary audio synchronization settings are restored')
 await cmd('script-message-to','modernz','osc-visibility','never');await sleep(1200)
 const images=['hidden.png','hint-first.png','hint-repeat.png'].map(n=>path.join(out,n))
 await cmd('screenshot-to-file',images[0],'window')
 for(const target of images.slice(1)){await cmd('script-message-to','cinema_input','cinema-toast','重复提示','测试');await sleep(150);await cmd('screenshot-to-file',target,'window');await sleep(1600)}
 const {execFile}=require('node:child_process'),execute=require('node:util').promisify(execFile)
 await execute(path.join(require('node:os').homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'),['-c','from PIL import Image, ImageChops; import sys; a,b,c=[Image.open(p).convert("RGB") for p in sys.argv[1:]]; assert ImageChops.difference(a,b).getbbox(); assert not ImageChops.difference(b,c).getbbox()',...images],{windowsHide:true})
 check(true,'identical hints render again after their cached overlay has been parked (pixel comparison)')
 await player.stop();const log=await fs.readFile(path.join(out,'state/player.log'),'utf8');check(!/Lua error|stack traceback|OSD update failed|Error loading script/.test(log),'no Lua or OSD errors');report.passed=true
 }finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}}
main().catch(e=>{console.error(e);process.exitCode=1})
