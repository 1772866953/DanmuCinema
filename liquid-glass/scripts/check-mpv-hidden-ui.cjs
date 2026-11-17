'use strict'
// No playback IPC polling during a sample: polling can itself keep rendering awake.
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{spawn,execFile}=require('node:child_process'),run=require('node:util').promisify(execFile)
const packaged=process.env.DANMU_PACKAGED_ROOT,{LocalPlayer}=require(packaged?path.join(packaged,'resources/app.asar/electron/player.cjs'):'../electron/player.cjs')
const out=path.resolve('artifacts/mpv-hidden-ui-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,phases:[]}
const player=new LocalPlayer({gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,runtime:process.env.DANMU_TEST_RUNTIME||(packaged?path.join(packaged,'resources/engine/runtime/mpv'):path.resolve('runtime/mpv')),scripts:packaged?path.join(packaged,'resources/player'):path.resolve('player'),storage:path.join(out,'state'),extraArgs:['--volume=0','--geometry=1100x700+40+40','--script-opts-append=modernz-minmousemove=100000','--script-opts-append=cinema-qa=yes',...(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)]})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
let rtss,samples=[],sequence=0
async function probe(){const id=String(++sequence);await cmd('script-message-to','cinema','cinema-probe',id);for(let i=0;i<60;i++){try{const p=await get('user-data/cinema-probe');if(p.sequence===id)return p}catch{}await sleep(10)}throw Error('Probe timeout')}
const quantile=(a,q)=>[...a].sort((a,b)=>a-b)[Math.min(a.length-1,Math.floor(a.length*q))]
async function main(){
 await fs.mkdir(out,{recursive:true})
 for(const helper of ['RtssProbe','MpvMotionProbe','MpvWindowProbe'])await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+path.join(out,helper+'.exe'),path.resolve('development/tests/'+helper+'.cs')],{windowsHide:true})
 const video=path.join(out,'dense.mkv'),seconds=process.argv.includes('--short')?8:25
 let source=path.resolve('artifacts/local-player/media/4K-HEVC10.mkv'),preferences={danmuFps:Number(process.env.DANMU_TEST_FPS)||0,fontSize:21,region:.15,resume:false}
 if(process.argv.includes('--real')){
  const installed='C:/Software/DanmuCinema',log=await fs.readFile(installed+'/data/player/player.log','utf8'),files=[...log.matchAll(/Run command: loadfile.*?url="((?:\\.|[^"\\])*)"/g)].map(m=>JSON.parse('"'+m[1]+'"')),index=Number(log.match(/Run command: playlist-play-index.*?index="?(\d+)/)?.[1]||0)
  source=process.env.DANMU_TEST_SOURCE||files[index];assert(source,'No selected file in installed log');preferences={...JSON.parse(await fs.readFile(installed+'/data/player/settings.json','utf8')),resume:false,volume:0}
 }
 await run(path.resolve('runtime/jellyfin/ffmpeg.exe'),['-v','error','-i',source,'-map','0','-c','copy','-t','240',video],{windowsHide:true})
 if(process.argv.includes('--real'))await fs.copyFile(path.join(path.dirname(source),path.parse(source).name+'.xml'),video.replace('.mkv','.xml'))
 else await fs.writeFile(video.replace('.mkv','.xml'),'<i>'+Array.from({length:7200},(_,i)=>`<d p="${i/30},1,25,16777215">隐藏界面连续运动 ${i} ABCDEFG 12345</d>`).join('')+'</i>')
 try{
  await player.configure(preferences)
  await player.play([{key:'test',path:video,title:'UI 隐藏对照测试'}],'test')
  for(let i=0;!player.state.danmuCount&&i<250;i++)await sleep(20)
  assert(player.state.danmuCount>0);await cmd('seek',10,'absolute+exact');await cmd('set_property','fullscreen',true);assert.equal(await get('fullscreen'),true);report.dimensions=await get('osd-dimensions');report.context=await get('current-gpu-context');report.decoder=await get('hwdec-current');if(process.argv.includes('--motion'))await cmd('script-message-to','cinema','cinema-test-marker','yes')
  rtss=spawn(path.join(out,'RtssProbe.exe'),[String(player.child.pid),String(5*(seconds+6)+30)],{windowsHide:true})
  let buffer='';rtss.stdout.on('data',b=>{buffer+=b;for(let n;(n=buffer.indexOf('\n'))>=0;){try{samples.push(JSON.parse(buffer.slice(0,n)))}catch{}buffer=buffer.slice(n+1)}})
  for(const visibility of ['hidden','osc','hidden','settings','hidden']){
   if(visibility==='settings')await cmd('script-binding','cinema/cinema-settings')
   else{if((await get('user-data/cinema-panel').catch(()=>({open:false}))).open)await cmd('keypress','ESC');await cmd('script-message-to','modernz','osc-visibility',visibility==='osc'?'always':process.argv.includes('--auto')?'auto':'never')}
   await cmd('set_property','fullscreen',true);if(visibility==='hidden'&&process.argv.includes('--auto')){await sleep(400);await cmd('script-message-to','modernz','osc-hide')}await run(path.join(out,'MpvWindowProbe.exe'),['activate',String(player.child.pid)],{windowsHide:true});await sleep(2000)
   assert.equal(await get('fullscreen'),true);const nativeWindow=JSON.parse((await run(path.join(out,'MpvWindowProbe.exe'),['activate',String(player.child.pid)],{windowsHide:true})).stdout);report.nativeWindow=nativeWindow;await cmd('script-message-to','modernz','cinema-osc-controls');await sleep(50)
   const ui=await get('user-data/osc/ui-stats'),panel=await get('user-data/cinema-panel').catch(()=>({open:false}))
   assert(visibility==='settings'?panel.open:visibility==='hidden'?!ui.visible:ui.visible,'wrong UI state')
   const display=await get('display-fps'),a=await probe(),traceFile=path.join(out,'trace-'+report.phases.length+'.json')
   await cmd('script-message-to','cinema','cinema-trace','start');samples=[]
   let motion;if(process.argv.includes('--motion'))motion=run(path.join(out,'MpvMotionProbe.exe'),[String(player.child.pid),String(seconds)],{windowsHide:true,maxBuffer:5*1024*1024});await sleep(seconds*1000) // Deliberately no MPV queries, mouse movement or screenshots.
   const motionFrames=motion?(await motion).stdout.trim().split(/\r?\n/).map(s=>JSON.parse(s)):[];await fs.writeFile(path.join(out,'motion-'+report.phases.length+'.json'),JSON.stringify(motionFrames));const motionPairs=motionFrames.slice(1).map((p,i)=>({before:motionFrames[i],after:p})).filter(p=>p.before.foreground&&p.after.foreground&&p.before.pixels>=35&&p.before.pixels<=45&&p.after.pixels>=35&&p.after.pixels<=45);const backwards=motionPairs.filter(p=>(p.after.left+p.after.right-p.before.left-p.before.right)/2>1&&(p.after.left+p.after.right-p.before.left-p.before.right)/2<100);assert.equal(await get('fullscreen'),true,'sample left fullscreen');const b=await probe(),physical=samples.slice();await cmd('script-message-to','cinema','cinema-trace','save',traceFile)
   for(let i=0;i<50;i++){if(await fs.access(traceFile).then(()=>true,()=>false))break;await sleep(10)}
   const trace=JSON.parse(await fs.readFile(traceFile,'utf8')),deltas=trace.frames.slice(1).map((f,i)=>({wall:f.at-trace.frames[i].at,time:f.time-trace.frames[i].time}))
   const valid=physical.filter(s=>!s.missing&&s.pid===player.child.pid),phase={visibility,display,ui,panelOpen:panel.open,seconds,paused:b.paused,clockError:b.time-b.nativeTime,updates:(b.overlay.completed-a.overlay.completed)/(b.elapsed-a.elapsed),rtss:physical,presentMean:valid.reduce((sum,s)=>sum+s.fps,0)/valid.length,presentMin:Math.min(...valid.map(s=>s.fps)),frameMsP99:quantile(deltas.map(d=>d.wall*1000),.99),maxFrameMs:Math.max(...deltas.map(d=>d.wall*1000)),backwards:deltas.filter(d=>d.time<0).length,resetEvents:trace.resets,screenForegroundSamples:motionFrames.filter(p=>p.foreground).length,screenValidPairs:motionPairs.length,screenBacksteps:backwards.length,screenSamples:motionFrames.length,screenBackstepExamples:backwards.slice(0,10)}
   report.phases.push(phase);console.log(JSON.stringify({...phase,rtss:undefined}))
  }
  report.passed=report.phases.length===5&&report.phases.every(p=>!p.paused&&p.backwards===0&&p.screenBacksteps===0&&Math.abs(p.clockError)<.15&&p.presentMean>=p.display*.99&&p.presentMin>=p.display*.97)
 }finally{rtss?.kill();await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(out)}
 if(!report.passed&&!process.argv.includes('--baseline'))process.exitCode=1
}
main().catch(e=>{console.error(e);process.exitCode=1})
