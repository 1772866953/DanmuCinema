'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{execFile}=require('node:child_process'),run=require('node:util').promisify(execFile)
const packaged=process.env.DANMU_PACKAGED_ROOT,{LocalPlayer}=require(packaged?path.join(packaged,'resources/app.asar/electron/player.cjs'):'../electron/player.cjs')
const out=path.resolve('artifacts/speed-transitions-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={cases:[],passed:false}
const player=new LocalPlayer({runtime:packaged?path.join(packaged,'resources/engine/runtime/mpv'):path.resolve('runtime/mpv'),scripts:packaged?path.join(packaged,'resources/player'):path.resolve('player'),storage:path.join(out,'state'),gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,extraArgs:['--volume=0','--script-opts-append=cinema-qa=yes','--script-opts-append=modernz-minmousemove=100000',...(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)]})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
const snapshot=async()=>Object.fromEntries(await Promise.all(['frame-drop-count','decoder-frame-drop-count','vo-delayed-frame-count','avsync','speed','time-pos','display-fps','audio-out-params','video-sync','hwdec-current','video-params','video-target-params'].map(async n=>[n,await get(n).catch(()=>null)])))
async function main(){await fs.mkdir(out,{recursive:true});const media=path.resolve(process.env.DANMU_TEST_MEDIA||'artifacts/speed-transition-media.mkv')
 try{await fs.access(media)}catch{await run(path.resolve('runtime/jellyfin/ffmpeg.exe'),['-v','error','-stream_loop','-1','-i',path.resolve(process.env.DANMU_TEST_SOURCE||'artifacts/local-player/media/4K-HEVC10.mkv'),'-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-map','0:v','-map','1:a','-c:v','copy','-c:a','aac','-t','180',media],{windowsHide:true});await fs.writeFile(media.replace('.mkv','.xml'),'<i>'+Array.from({length:5400},(_,i)=>`<d p="${i/30},1,25,16777215">速度切换测试 ${i}</d>`).join('')+'</i>')}
 try{await player.configure({danmuFps:0,resume:false,fontSize:21,region:.15});await player.play([{key:'test',path:media,title:'倍速切换边界测试'}],'test');for(let i=0;i<300&&!player.state.danmuCount;i++)await sleep(20);await sleep(1200)
 const pid=player.child.pid,helper=path.join(out,'MpvWindowProbe.exe');await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+helper,path.resolve('development/tests/MpvWindowProbe.cs')],{windowsHide:true})
 for(const mode of (process.env.DANMU_TEST_MODES||'fullscreen,window,maximized').split(',')){
  await cmd('set_property','fullscreen',false);await run(helper,[mode==='maximized'?'maximize':'restore',String(pid)],{windowsHide:true});if(mode==='fullscreen')await cmd('set_property','fullscreen',true)
  for(const speed of (process.env.DANMU_TEST_SPEEDS||'2,4,6').split(',').map(Number)){
   await player.configure({holdSpeed:speed});await cmd('set_property','time-pos',12);await cmd('script-message-to','modernz','osc-visibility','never');await sleep(1500)
   for(let repeat=0;repeat<2;repeat++){
    const before=await snapshot(),samples=[],events=[];const onEvent=e=>{if(['seek','playback-restart'].includes(e.event))events.push(e.event)};player.ipc.on('event',onEvent);const start=performance.now();await cmd('keydown','RIGHT')
    for(let i=0;i<15;i++){await sleep(40);samples.push({at:performance.now()-start,...await snapshot()})}
    const held=await snapshot();await cmd('keyup','RIGHT');const released=performance.now()-start
    for(let i=0;i<25;i++){await sleep(40);samples.push({at:performance.now()-start,...await snapshot()})}
    const after=await snapshot();player.ipc.off('event',onEvent);const result={mode,speed,repeat,drops:after['frame-drop-count']-before['frame-drop-count'],entryDrops:held['frame-drop-count']-before['frame-drop-count'],exitDrops:after['frame-drop-count']-held['frame-drop-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],delayed:after['vo-delayed-frame-count']-before['vo-delayed-frame-count'],avsync:after.avsync,released,before,after,samples,events};report.cases.push(result);console.log(JSON.stringify({...result,before:undefined,after:undefined,samples:undefined}))
   }
  }
 }
 report.passed=report.cases.every(c=>c.drops===0&&c.decoderDrops===0&&Math.abs(c.avsync)<.05&&c.after.speed===1&&c.events.length===0)
 }finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed}))}
}main().catch(e=>{console.error(e);process.exitCode=1})
