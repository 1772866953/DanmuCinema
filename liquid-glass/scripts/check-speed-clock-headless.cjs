'use strict'
// Clock/transport diagnostics only. No window, GPU presentation or audio device
// is created. These results must never be used as a frame-pacing acceptance.
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process')
const {MpvIpc}=require('../electron/mpv-ipc.cjs')
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),out=path.resolve('artifacts/speed-clock-headless-'+Date.now())
async function run(mode){
 const ipc=new MpvIpc(),pipe='\\\\.\\pipe\\DanmuClock-'+crypto.randomUUID(),events=[]
 const child=spawn(path.resolve('runtime/mpv/mpv.exe'),['--no-config','--load-scripts=no','--load-select=no','--terminal=no','--input-terminal=no','--vo=null','--ao=null','--ao-null-buffer=0.03','--force-window=no','--fullscreen=no','--window-minimized=yes','--focus-on=never','--hwdec=no','--volume=0','--idle=yes','--audio-buffer=0.05',...(mode.endsWith('Persistent')?['--af=scaletempo2']:[]),`--input-ipc-server=${pipe}`,`--script=${path.resolve('player/cinema.lua')}`,'--script-opts=cinema-vblank_clock=no',`--log-file=${path.join(out,mode+'.log')}`],{windowsHide:true,stdio:'ignore'})
 const exit=new Promise(resolve=>child.once('exit',resolve))
 const command=(...args)=>ipc.command(args)
 ipc.on('event',e=>{if(['seek','playback-restart','audio-reconfig','video-reconfig'].includes(e.event))events.push({at:performance.now(),event:e.event})})
 try{
  await ipc.connect(pipe);await command('loadfile',path.resolve('artifacts/real-movie-sample.mkv'));await sleep(1200)
  const rows=[],phases=[]
  for(const target of [6,1,2,1]){
   const start=performance.now();phases.push({target,at:start});events.length=0
   if(mode==='direct')await command('set_property','speed',target)
   else await command('script-message-to','cinema','cinema-speed',String(target))
   while(performance.now()-start<1100){
    const state=await command('expand-text','${=speed}|${=audio-pts}|${=time-pos}|${=avsync}|${video-sync}');const [speed,audio,video,avsync,sync]=state.split('|');rows.push({at:performance.now(),target,speed:Number(speed),audio:Number(audio),video:Number(video),avsync:Number(avsync),sync});await sleep(15)
   }
   phases.at(-1).events=[...events]
  }
  const changes=phases.map((p,i)=>{const part=rows.filter(r=>r.at>=p.at&&r.at<(phases[i+1]?.at||Infinity));return {target:p.target,settledMs:part.find(r=>r.speed===p.target)?.at-p.at,maxAbsAvsync:Math.max(...part.map(r=>Math.abs(r.avsync))),audioBacksteps:part.filter((r,j)=>j&&r.audio<part[j-1].audio-.001).length,events:p.events.map(e=>e.event)}})
  await fs.writeFile(path.join(out,mode+'.json'),JSON.stringify({mode,rows,phases,changes},null,2));return{mode,changes}
 }finally{await command('quit').catch(()=>{});ipc.close();await Promise.race([exit,sleep(1500)]);if(child.exitCode===null)child.kill()}
}
async function main(){await fs.mkdir(out,{recursive:true});const results=[];for(const mode of ['direct','cinema','cinemaPersistent'])results.push(await run(mode));const report={mode:'null video and null audio; clock diagnostics only',performanceVerified:false,results};await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report},null,2))}
main().catch(e=>{console.error(e);process.exitCode=1})
