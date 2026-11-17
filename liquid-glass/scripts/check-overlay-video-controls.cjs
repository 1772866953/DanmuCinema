'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{execFile}=require('node:child_process'),run=require('node:util').promisify(execFile)
const {LocalPlayer}=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs'),sleep=ms=>new Promise(r=>setTimeout(r,ms)),out=path.resolve('artifacts/overlay-video-controls-'+Date.now()),report={passed:false,cases:[],focus:[]}
const p=new LocalPlayer({runtime:path.resolve('runtime/mpv'),scripts:path.resolve('player'),storage:path.join(out,'state'),createOverlay:value=>new PlayerOverlay({...value,base:process.cwd(),executable:path.resolve('tools/electron/electron.exe')}),extraArgs:['--fullscreen=no','--pause=yes','--volume=0','--geometry=960x540+50+50','--script-opts-append=cinema-qa=yes']})
const cmd=(...a)=>p.ipc.command(a),get=n=>cmd('get_property',n),panel=()=>get('user-data/cinema-panel')
async function wait(fn){for(let n=0;n<200;n++){if(await fn())return;await sleep(15)}throw Error('Timed out')}
async function main(){await fs.mkdir(out,{recursive:true});const helper=path.join(out,'Window.exe');await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/target:exe','/platform:x64','/out:'+helper,path.resolve('development/tests/MpvWindowProbe.cs')],{windowsHide:true})
 try{
  await p.play([{key:'test',path:path.resolve('artifacts/real-movie-sample.mkv'),title:'比例控件检查'}],'test');await wait(()=>p.current&&p.state.danmuCount);await cmd('observe_property',990,'focused');p.ipc.on('event',e=>{if(e.event==='property-change'&&e.name==='focused')report.focus.push({ms:performance.now(),value:e.data})})
  for(const input of ['win32','ipc'])for(let repeat=0;repeat<4;repeat++){
   await cmd('script-message-to','select','cinema-settings','video');await wait(async()=>{const s=await panel();return s.open&&s.tab==='video'&&s.contentOpacity===1&&!s.animating});const before=await panel(),c=before.controls.find(c=>c.key==='aspect3'),x=Math.round((c.x+c.w/2)*before.pixelScale),y=Math.round((c.y+c.h/2)*before.pixelScale)
   const first=await get('video-aspect-override');if(input==='win32')await run(helper,['click',String(p.child.pid),String(x),String(y)],{windowsHide:true});else await cmd('mouse',x,y,0,'single');await sleep(250)
   const after=await panel(),value=await get('video-aspect-override'),item={input,repeat,x,y,before:before.rect,pixelScale:before.pixelScale,was:first,value,open:after.open,tab:after.tab,mouse:await get('mouse-pos'),focused:await get('focused'),dimensions:await get('osd-dimensions')};item.passed=Math.abs(value-4/3)<.001;report.cases.push(item);console.log(JSON.stringify(item))
   if(after.open){await cmd('keypress','ESC');await wait(async()=>!(await panel()).open&&!(await panel()).fading)}await cmd('set_property','video-aspect-override',-1);await sleep(250)
  }
  report.passed=report.cases.every(c=>c.passed)
 }catch(error){report.error=error.stack;process.exitCode=1}finally{await p.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed,error:report.error}))}
}
main().catch(e=>{console.error(e);process.exitCode=1})
