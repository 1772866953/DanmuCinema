'use strict'
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict')
const root=process.env.DANMU_PACKAGED_ROOT,{LocalPlayer}=require(root?path.join(root,'resources/app.asar/electron/player.cjs'):'../electron/player.cjs'),out=path.resolve('artifacts/display-follow-'+Date.now()),sleep=ms=>new Promise(r=>setTimeout(r,ms)),report={passed:false,cases:[]}
const player=new LocalPlayer({runtime:root?path.join(root,'resources/engine/runtime/mpv'):path.resolve('runtime/mpv'),scripts:root?path.join(root,'resources/player'):path.resolve('player'),storage:path.join(out,'state'),gpuAdapter:()=>process.env.DANMU_TEST_ADAPTER||null,extraArgs:['--pause=yes','--volume=0','--fullscreen=no']})
const cmd=(...a)=>player.ipc.command(a),get=n=>cmd('get_property',n)
let serial=0
async function probe(){const id=String(++serial);await cmd('script-message-to','cinema','cinema-probe',id);for(let i=0;i<100;i++){const p=await get('user-data/cinema-probe').catch(()=>null);if(p?.sequence===id)return p;await sleep(10)}throw Error('probe timeout')}
async function main(){await fs.mkdir(out,{recursive:true});try{
 await player.configure({danmuFps:240});assert.equal(player.settings.danmuFps,0)
 await player.play([{key:'test',title:'显示刷新率适配测试',path:path.resolve('artifacts/local-player/media/第一集 [fixture].mkv')}],'test');await sleep(800);report.physical=await get('display-fps')
 for(const rate of [59.94,144,240,360]){await cmd('set_property','display-fps-override',rate);await sleep(120);const p=await probe();assert.equal(p.fps,0);assert(Math.abs(p.effectiveFps-rate)<.02,`${p.effectiveFps} != ${rate}`);report.cases.push({simulatedDisplay:rate,effective:p.effectiveFps})}
 await cmd('set_property','display-fps-override',0);await sleep(120)
 for(const fps of [60,120,0]){await player.configure({danmuFps:fps});await sleep(100);const p=await probe(),expected=fps===0?report.physical:Math.min(fps,report.physical);assert.equal(p.fps,fps);assert(Math.abs(p.effectiveFps-expected)<.02);report.cases.push({setting:fps,effective:p.effectiveFps})}
 report.passed=true
}finally{await player.stop();await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({out,...report}))}}
main().catch(e=>{console.error(e);process.exitCode=1})
