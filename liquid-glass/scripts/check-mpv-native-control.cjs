'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process'),{MpvIpc}=require('../electron/mpv-ipc.cjs')
const out=path.resolve('artifacts/mpv-native-control-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={cases:[],nativeBinaryUnchanged:false}
async function run(sync){
 const ipc=new MpvIpc(),pipe='\\\\.\\pipe\\DanmuNativeControl-'+crypto.randomUUID()
 const child=spawn(path.resolve('runtime/mpv/mpv.exe'),['--no-config','--load-scripts=no','--load-select=no','--osc=no','--osd-level=0','--osd-bar=no','--terminal=no','--input-terminal=no','--fullscreen=yes','--idle=yes','--force-window=immediate','--vo=gpu-next','--gpu-api=d3d11','--gpu-context=d3d11','--hwdec=auto-safe','--d3d11va-zero-copy=no',`--video-sync=${sync}`,'--volume=0','--title=原版 MPV 播放对照',`--input-ipc-server=${pipe}`,`--log-file=${path.join(out,sync+'.log')}`],{windowsHide:true,stdio:'ignore'})
 const closed=new Promise(r=>child.once('exit',r)),cmd=(...a)=>ipc.command(a),get=n=>cmd('get_property',n)
 try{
  await ipc.connect(pipe);await cmd('loadfile',path.resolve('artifacts/real-movie-sample.mkv'));await sleep(1800)
  const sample=async()=>({drops:await get('frame-drop-count'),decodeDrops:await get('decoder-frame-drop-count'),avsync:await get('avsync'),speed:await get('speed')})
  for(const speed of [2,6]){
   await cmd('set_property','time-pos',12);await sleep(1400)
   const before=await sample();await cmd('set_property','speed',speed);await sleep(2500);const held=await sample();await cmd('set_property','speed',1);await sleep(1400);const after=await sample()
   const result={sync,speed,entryDrops:held.drops-before.drops,exitDrops:after.drops-held.drops,decoderDrops:after.decodeDrops-before.decodeDrops,heldSpeed:held.speed,finalSpeed:after.speed,finalAvsync:after.avsync};report.cases.push(result);console.log(JSON.stringify(result))
  }
 }finally{await cmd('quit').catch(()=>{});ipc.close();await Promise.race([closed,sleep(2000)]);if(child.exitCode===null)child.kill()}
}
async function main(){await fs.mkdir(out,{recursive:true});report.nativeBinaryUnchanged=crypto.createHash('sha256').update(await fs.readFile('runtime/mpv/mpv.exe')).digest('hex').toUpperCase()===JSON.parse(await fs.readFile('runtime/mpv/build.json')).executable;for(const sync of ['audio','display-resample'])await run(sync);await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report}))}
main().catch(e=>{console.error(e);process.exitCode=1})
