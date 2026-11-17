'use strict'
const fs=require('node:fs/promises'),path=require('node:path'),{spawn}=require('node:child_process')
async function main(){
 const report=path.resolve(process.env.DANMU_INPUT_TEST_REPORT||'artifacts/player-input-headless.json'),root=path.resolve(process.env.DANMU_INPUT_TEST_ROOT||'.').replaceAll('\\','/')
 const child=spawn(path.resolve('runtime/mpv/mpv.exe'),['--no-config','--load-scripts=no','--load-select=no','--terminal=no','--input-terminal=no','--vo=null','--ao=null','--force-window=no','--fullscreen=no','--focus-on=never','--idle=yes',`--script=${path.resolve('tests/player-input-headless.lua')}`,`--script-opts=cinema-vblank_clock=no,input-test-root=${root},input-test-report=${report.replaceAll('\\','/')}`],{windowsHide:true,stdio:'ignore'})
 const timer=setTimeout(()=>child.kill(),10000)
 try{const code=await new Promise((resolve,reject)=>{child.once('exit',resolve);child.once('error',reject)});const result=JSON.parse(await fs.readFile(report,'utf8'));console.log(JSON.stringify(result,null,2));if(code||!result.passed)process.exitCode=1}finally{clearTimeout(timer)}
}
main().catch(error=>{console.error(error);process.exitCode=1})
