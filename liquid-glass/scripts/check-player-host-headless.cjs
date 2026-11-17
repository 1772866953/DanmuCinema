'use strict'
const fs=require('node:fs/promises'),path=require('node:path'),{spawn}=require('node:child_process')
async function main(){
 const report=path.resolve(process.env.DANMU_HOST_TEST_REPORT||'artifacts/player-host-headless.json'),script=path.resolve(process.env.DANMU_HOST_TEST_SOURCE||'player/cinema-host.lua')
 const child=spawn(path.resolve('runtime/mpv/mpv.exe'),['--no-config','--load-scripts=no','--load-select=no','--terminal=no','--input-terminal=no','--vo=null','--ao=null','--force-window=no','--fullscreen=no','--idle=yes',`--script=${path.resolve('tests/player-host-headless.lua')}`,`--script-opts=host-test-script=${script.replaceAll('\\','/')},host-test-report=${report.replaceAll('\\','/')}`],{windowsHide:true,stdio:'ignore'})
 const timer=setTimeout(()=>child.kill(),10000)
 try{const code=await new Promise((resolve,reject)=>{child.once('exit',resolve);child.once('error',reject)}),result=JSON.parse(await fs.readFile(report,'utf8'));console.log(JSON.stringify(result));if(code||!result.passed)process.exitCode=1}finally{clearTimeout(timer)}
}
main().catch(error=>{console.error(error);process.exitCode=1})
