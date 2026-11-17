'use strict'
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict')
const {spawn}=require('node:child_process')
const packaged=process.env.DANMU_PACKAGED_ROOT,root=packaged?path.join(packaged,'resources/app.asar/electron'):path.resolve('electron'),{LocalPlayer}=require(path.join(root,'player.cjs'))
const out=path.resolve('artifacts/mpv-render-backend-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,formats:[],comparisons:[],fallback:null}
const runtime=packaged?path.join(packaged,'resources/engine/runtime/mpv'):path.resolve('runtime/mpv'),scripts=packaged?path.join(packaged,'resources/player'):path.resolve('player')
async function wait(fn,label){for(let i=0;i<250;i++){try{if(await fn())return}catch{}await sleep(20)}throw Error('Timeout '+label)}
async function main(){
 await fs.mkdir(out,{recursive:true});const {runtime:load}=await import('./test-runtime.mjs'),sharp=load('sharp')
 const player=new LocalPlayer({gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,runtime,scripts,storage:path.join(out,'state'),extraArgs:['--fullscreen=no','--geometry=960x540+50+50','--volume=0','--pause=yes']})
 const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
 async function shot(label){await cmd('seek',1,'absolute+exact');await wait(async()=>!await get('seeking')&&Math.abs(await get('time-pos')-1)<.2,'seek');await sleep(180);const file=path.join(out,label+'.png');await cmd('screenshot-to-file',file,'video');return file}
 try{
  for(const name of (process.argv.includes('--fallback-only')?[]:['第一集 [fixture].mkv','SD-601-4by3.mkv','H264-10bit.mkv','HEVC-HDR10.mkv','AV1-10bit.mkv','full-range.mp4','VFR.mkv','rotated.mp4','4K-HEVC10.mkv'])){
   await player.configure({hardware:true});await player.play([{key:'test',title:'隔离解码校验',path:path.resolve('artifacts/local-player/media',name)}],'test')
   await wait(()=>player.state.video?.w&&player.state.output?.w,'decode '+name)
   const input=await get('video-params'),output=await get('video-out-params'),target=await get('video-target-params'),context=await get('current-gpu-context'),decoder=await get('hwdec-current')
   assert.equal(context,'d3d11');assert.equal(await get('interpolation'),false);assert.equal(await get('video-output-levels'),'auto');assert(!player.state.error)
   report.formats.push({name,context,decoder,input,output,target})
   if(['第一集 [fixture].mkv','HEVC-HDR10.mkv','4K-HEVC10.mkv'].includes(name)){
    const index=report.formats.length,hw=await shot(index+'-hardware');await player.configure({hardware:false});await wait(async()=>await get('hwdec-current')==='no','software decoder');const sw=await shot(index+'-software')
    const a=await sharp(hw).removeAlpha().raw().toBuffer({resolveWithObject:true}),b=await sharp(sw).removeAlpha().raw().toBuffer({resolveWithObject:true});assert.deepEqual(a.info,b.info);let total=0,different=0
    for(let i=0;i<a.data.length;i++){const d=Math.abs(a.data[i]-b.data[i]);total+=d;if(d>4)different++}
    const result={name,meanError:total/a.data.length,channelsOver4:different/a.data.length,width:a.info.width,height:a.info.height};report.comparisons.push(result);assert(result.meanError<1&&result.channelsOver4<.01,'hardware/software image mismatch '+JSON.stringify(result))
   }
  }
 }finally{await player.stop()}
 const fallback=new LocalPlayer({gpuAdapter:()=> 'DanmuCinema QA missing adapter',runtime,scripts,storage:path.join(out,'fallback'),spawnPlayer:(exe,args,options)=>spawn(exe,args,{...options,env:{...process.env}}),extraArgs:['--fullscreen=no','--pause=yes','--volume=0']})
 try{
  await fallback.play([{key:'test',path:path.resolve('artifacts/local-player/media/第一集 [fixture].mkv'),title:'渲染兼容回退检查'}],'test');await wait(()=>fallback.state.video?.w&&fallback.state.output?.w,'fallback decode')
  const context=await fallback.ipc.command(['get_property','current-gpu-context']);assert.equal(context,'d3d11');report.fallback={context,active:fallback.state.active,missingAdapterIgnored:true};assert(fallback.state.active&&!fallback.state.error)
 }finally{await fallback.stop()}
 const alternative=new LocalPlayer({runtime,scripts,storage:path.join(out,'vulkan'),extraArgs:['--fullscreen=no','--pause=yes','--volume=0','--gpu-api=vulkan','--gpu-context=winvk']});try{await alternative.play([{key:'test',path:path.resolve('artifacts/local-player/media/第一集 [fixture].mkv'),title:'备用后端检查'}],'test');await wait(()=>alternative.state.output?.w,'alternative decode');assert.equal(await alternative.ipc.command(['get_property','current-gpu-context']),'winvk');report.fallback.alternative='winvk'}finally{await alternative.stop()}
 report.passed=true
}
main().catch(error=>{report.error=error.stack;process.exitCode=1}).finally(async()=>{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,passed:report.passed,formats:report.formats.length,comparisons:report.comparisons,fallback:report.fallback,error:report.error}))})
