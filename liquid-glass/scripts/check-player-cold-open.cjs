'use strict'
require('./foreground-test-guard.cjs')()
const fs=require('node:fs/promises'),path=require('node:path'),{execFile}=require('node:child_process'),run=require('node:util').promisify(execFile)
const {LocalPlayer}=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs')
const out=path.resolve('artifacts/player-cold-open-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={samples:[],errors:[]}
;(async()=>{
 await fs.mkdir(out,{recursive:true});const helper=path.join(out,'State.exe'),windowHelper=path.join(out,'Window.exe')
 await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/out:'+helper,path.resolve('development/tests/OverlayStateProbe.cs')],{windowsHide:true})
 await run(path.join(process.env.WINDIR,'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),['/nologo','/out:'+windowHelper,path.resolve('development/tests/MpvWindowProbe.cs')],{windowsHide:true})
 const root=process.env.DANMU_PACKAGED_ROOT,player=new LocalPlayer({runtime:path.resolve(root?path.join(root,'resources/engine/runtime/mpv'):'runtime/mpv'),scripts:path.resolve(root?path.join(root,'resources/player'):'player'),storage:path.join(out,'state'),createOverlay:o=>new PlayerOverlay({...o,base:root?path.join(root,'resources/app.asar'):process.cwd(),packaged:!!root,executable:root?path.join(root,'DanmuCinema.exe'):path.resolve('tools/electron/electron.exe')}),extraArgs:['--volume=0']})
 try{
  if(process.env.DANMU_INITIAL_MODE==='window')await player.configure({fullscreen:false})
  const start=performance.now();await player.play([{key:'test',title:'初始播放验收',path:path.resolve('artifacts/overlay-4k-fixture/test.mkv')}],'test');report.openMs=performance.now()-start
  for(let n=0;n<100&&!player.current;n++)await sleep(50)
  report.loadedMs=performance.now()-start
  const native=async(action,...args)=>run(windowHelper,[action,String(player.child.pid),...args.map(String)],{windowsHide:true}),cmd=(...args)=>player.ipc.command(args)
  for(const action of ['initial','right-menu','hide-menu','window','right-menu-window','hide-menu','minimize','restore','blur','focus','right-menu-window','hide-menu','maximize','right-menu-maximized','hide-menu','fullscreen','right-menu-fullscreen','hide-menu','window','1080p','right-menu-1080p','hide-menu','1440p','right-menu-1440p','hide-menu']){
   if(action.startsWith('right-menu'))await cmd('keypress','MBTN_RIGHT')
   if(action==='hide-menu')await cmd('keypress','ESC')
   if(action==='window')await cmd('set_property','fullscreen',false)
   if(['minimize','restore','maximize','blur','focus'].includes(action))await native(action)
   if(action==='fullscreen')await cmd('set_property','fullscreen',true)
   if(action==='1080p'){await native('restore');await native('resize',1920,1080)}
   if(action==='1440p')await native('resize',2560,1440)
   await sleep(450)
   const state=JSON.parse((await run(helper,[String(player.child.pid),player.overlay.hwnd],{windowsHide:true})).stdout)
   const ui=await player.overlay.probe(),dim=await cmd('get_property','osd-dimensions'),layers=ui.ui.layers.filter(x=>!x.hidden)
   const sample={action,...state,ui:ui.ui,dimensions:dim}
   sample.passed=state.owned&&(action==='minimize'?state.minimized&&!state.visible:state.visible&&state.aboveOwner)&&ui.ui.canvasCount===4&&ui.ui.errors.length===0
   if(action.startsWith('right-menu')){
    sample.metrics=await player.overlay.diagnose()
    sample.passed&&=layers.length>0&&layers.every(x=>Math.abs(x.backing[0]-dim.w)<1&&Math.abs(x.backing[1]-dim.h)<1)&&sample.metrics.compositedPixels>1000
    await fs.copyFile(await player.overlay.capture(),path.join(out,action+'.png'))
   }
   report.samples.push(sample)
  }
  report.passed=report.samples.every(x=>x.passed)
 }catch(error){report.errors.push(error.stack)}finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed,openMs:report.openMs,loadedMs:report.loadedMs,samples:report.samples.map(x=>({action:x.action,passed:x.passed})),errors:report.errors}));if(!report.passed)process.exitCode=1}
})().catch(e=>{console.error(e);process.exitCode=1})
