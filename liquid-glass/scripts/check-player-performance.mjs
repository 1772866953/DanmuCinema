import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import fs from 'node:fs/promises'
import path from 'node:path'
import {spawn,execFile} from 'node:child_process'
import {promisify} from 'node:util'
import assert from 'node:assert/strict'
import {runtime} from './test-runtime.mjs'
const run=promisify(execFile),sleep=ms=>new Promise(r=>setTimeout(r,ms)),{_electron}=runtime('playwright'),out=path.resolve('artifacts/player-performance-'+Date.now()),report={phases:[],passed:false}
await fs.mkdir(out,{recursive:true});const hook=path.join(out,'hook.cjs');await fs.writeFile(hook,'module.exports=api=>{global.__perfPlayer=api.getPlayer();global.__perfApp=api}')
for(const helper of ['RtssProbe','MpvWindowProbe'])await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+path.join(out,helper+'.exe'),path.resolve('development/tests/'+helper+'.cs')],{windowsHide:true})
const video=path.join(out,'4K60.mkv');await run(path.resolve('runtime/jellyfin/ffmpeg.exe'),['-v','error','-stream_loop','-1','-i',path.resolve('artifacts/local-player/media/4K-HEVC10.mkv'),'-f','lavfi','-i','anullsrc=r=48000:cl=stereo','-map','0:v','-map','1:a','-c:v','copy','-c:a','aac','-t','240',video],{windowsHide:true});await fs.writeFile(video.replace('.mkv','.xml'),'<i>'+Array.from({length:7200},(_,i)=>`<d p="${i/30},1,25,16777215">连续播放与弹幕测试 ${i} ABCDEFG</d>`).join('')+'</i>')
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs'),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const packaged=process.env.DANMU_PACKAGED_ROOT,app=await _electron.launch({executablePath:packaged?path.join(packaged,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe'),args:[...(packaged?[]:['.']),'--qa'],env,timeout:40000});let rtss,samples=[]
const cmd=(...args)=>app.evaluate((_,args)=>global.__perfPlayer.ipc.command(args),args),get=name=>cmd('get_property',name),configure=value=>app.evaluate((_,value)=>global.__perfPlayer.configure(value),value)
const properties=['time-pos','avsync','frame-drop-count','decoder-frame-drop-count','vo-delayed-frame-count','current-gpu-context','hwdec-current','display-fps','estimated-display-fps','osd-dimensions','pause']
const snapshot=()=>app.evaluate(async(_,names)=>Object.fromEntries(await Promise.all(names.map(async n=>[n,await global.__perfPlayer.ipc.command(['get_property',n]).catch(()=>null)]))),properties)
const probe=()=>app.evaluate(async()=>{const ipc=global.__perfPlayer.ipc,id=String(Date.now());await ipc.command(['script-message-to','cinema','cinema-probe',id]);for(let i=0;i<80;i++){const p=await ipc.command(['get_property','user-data/cinema-probe']).catch(()=>null);if(p?.sequence===id)return p;await new Promise(r=>setTimeout(r,5))}throw Error('probe timeout')})
try{
 const page=await app.firstWindow();await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true');report.gpu=await app.evaluate(async({app})=>(await app.getGPUInfo('complete')).gpuDevice);report.display=await app.evaluate(({screen,BrowserWindow})=>screen.getDisplayMatching(BrowserWindow.getAllWindows()[0].getBounds()))
 await configure({danmuFps:0,resume:false,fontSize:21,region:.15});await app.evaluate((_,{video,extra})=>{const p=global.__perfPlayer;p.extraArgs=['--volume=0','--geometry=1100x700+40+40','--script-opts-append=cinema-qa=yes','--script-opts-append=modernz-minmousemove=100000',...extra];return p.play([{key:'test',path:video,title:'4K60 综合性能测试'}],'test')},{video,extra:(process.env.DANMU_FPS_ARGS||'').split('|').filter(Boolean)})
 for(let i=0;i<200;i++){if(await app.evaluate(()=>global.__perfPlayer.state.danmuCount))break;await sleep(25)}
 await app.evaluate(()=>{const p=global.__perfPlayer,original=p.ipc.disconnect.bind(p.ipc);p.ipc.disconnect=e=>{p.__disconnect={message:e.message,stack:new Error().stack};return original(e)}})
 const pid=await app.evaluate(()=>global.__perfPlayer.child.pid);report.pid=pid;report.context=await get('current-gpu-context');assert.equal(report.context,'d3d11')
 rtss=spawn(path.join(out,'RtssProbe.exe'),[String(pid),'700'],{windowsHide:true});let buffer='';rtss.stdout.on('data',b=>{buffer+=b;for(let n;(n=buffer.indexOf('\n'))>=0;){try{samples.push(JSON.parse(buffer.slice(0,n)))}catch{}buffer=buffer.slice(n+1)}})
 const matrix=(process.argv.includes('--quick')?[0]:[60,120,0]).flatMap(fps=>['fullscreen','window','maximized'].flatMap(mode=>['hidden','settings'].map(ui=>({fps,mode,ui}))))
 for(const {fps,mode,ui}of matrix){
  await configure({danmuFps:fps});await cmd('set_property','fullscreen',false);await run(path.join(out,'MpvWindowProbe.exe'),[mode==='maximized'?'maximize':'restore',String(pid)],{windowsHide:true});if(mode==='fullscreen')await cmd('set_property','fullscreen',true);await run(path.join(out,'MpvWindowProbe.exe'),['activate',String(pid)],{windowsHide:true})
  await cmd('script-message-to','modernz','osc-visibility','never');if(ui==='settings')await cmd('script-binding','cinema/cinema-settings');await cmd('seek',10,'absolute+exact');await sleep(1400)
  if(ui==='hidden'){await cmd('set_property','pause',true);await sleep(80);await cmd('set_property','pause',false);for(const action of ['blur','focus'])await run(path.join(out,'MpvWindowProbe.exe'),[action,String(pid)],{windowsHide:true});await sleep(500)}
  // RTSS reports a rolling one-second window. Let the previous resize/seek
  // period expire before starting the steady-playback sample.
  await sleep(1500)
  const before=await snapshot(),a=await probe();assert.equal(a.fps,fps);const start=Date.now();samples=[];await sleep(Number(process.env.DANMU_TEST_SECONDS||10)*1000);const after=await snapshot(),b=await probe(),seconds=(Date.now()-start)/1000,valid=samples.filter(s=>!s.missing),phase={fps,mode,ui,seconds,display:after['display-fps'],videoDrops:after['frame-drop-count']-before['frame-drop-count'],decoderDrops:after['decoder-frame-drop-count']-before['decoder-frame-drop-count'],delayed:after['vo-delayed-frame-count']-before['vo-delayed-frame-count'],mediaAdvance:after['time-pos']-before['time-pos'],avsync:after.avsync,presentMean:valid.reduce((sum,s)=>sum+s.fps,0)/valid.length,presentMin:Math.min(...valid.map(s=>s.fps)),updates:(b.overlay.completed-a.overlay.completed)/(b.elapsed-a.elapsed),clockError:b.time-b.nativeTime,backsteps:b.clock.backwards-a.clock.backwards,before,after,rtss:samples.slice()};report.phases.push(phase);console.log(JSON.stringify({...phase,before:undefined,after:undefined,rtss:undefined}));assert(!after.pause);if(ui==='settings'){await cmd('keypress','ESC');await sleep(400)}
 }
 // A delayed VO refresh slot is not a dropped decoded video frame. Bound
 // refresh-slot jitter to 0.25%, while requiring zero actual video drops.
 report.passed=report.phases.every(p=>p.videoDrops===0&&p.decoderDrops===0&&p.delayed<=Math.ceil(p.seconds*p.display*.0025)&&Math.abs(p.mediaAdvance-p.seconds)<.15&&Math.abs(p.avsync)<.05&&p.presentMean>=p.display*.97&&p.presentMin>=p.display*.9&&p.updates>=(p.fps===0?p.display:Math.min(p.fps,p.display))*.95&&p.backsteps===0&&Math.abs(p.clockError)<.15)
 assert(report.passed,'Video/danmu presentation criteria failed')
}catch(e){report.error=e.stack;report.failure=await app.evaluate(()=>{const p=global.__perfPlayer;return {error:p.state.error,disconnect:p.__disconnect,closed:p.ipc?.closed,buffer:p.ipc?.buffer.length,pending:p.ipc?.pending.size,pid:p.child?.pid,exit:p.child?.exitCode}}).catch(()=>null);process.exitCode=1}finally{rtss?.kill();await app.evaluate(()=>global.__perfPlayer.stop()).catch(()=>{});await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,passed:report.passed,error:report.error,failure:report.failure}))}
