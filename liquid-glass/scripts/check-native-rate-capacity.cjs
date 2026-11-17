'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{LocalPlayer}=require('../electron/player.cjs')
const out=path.resolve('artifacts/native-rate-capacity-'+Date.now()),report={overlay:false,cases:[]},sleep=ms=>new Promise(r=>setTimeout(r,ms))
const player=new LocalPlayer({runtime:path.resolve('runtime/mpv'),scripts:path.resolve('player'),storage:path.join(out,'state'),extraArgs:['--volume=0']})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
async function sample(){return Object.fromEntries(await Promise.all(['frame-drop-count','decoder-frame-drop-count','time-pos','display-fps','speed','avsync','hwdec-current','estimated-vf-fps'].map(async n=>[n,await get(n)])))}
async function main(){await fs.mkdir(out,{recursive:true});report.mpvSha256=crypto.createHash('sha256').update(await fs.readFile('runtime/mpv/mpv.exe')).digest('hex')
try{
 await player.configure({enabled:false,resume:false,volume:0});await player.play([{key:'test',path:path.resolve('artifacts/overlay-4k-fixture/test.mkv'),title:'原生倍速容量对照'}],'test')
 await cmd('script-message-to','modernz','osc-visibility','never');await sleep(1200)
 for(const speed of [2,3,4,5,6]){
  await player.configure({holdSpeed:speed});await cmd('seek',20,'absolute+exact');await sleep(900);const before=await sample()
  await cmd('keydown','RIGHT');await sleep(1200);const held=await sample();await cmd('keyup','RIGHT');await sleep(1200);const after=await sample()
  report.cases.push({speed,before,held,after,entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count']})
 }
}catch(e){report.error=e.stack;process.exitCode=1}finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}}
main().catch(e=>{console.error(e);process.exitCode=1})
