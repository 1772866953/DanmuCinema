import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {spawnSync} from 'node:child_process'

const out=path.resolve('artifacts/native-login-items');await fs.mkdir(out,{recursive:true})
const name='DanmuCinema.AutostartProbe.'+Date.now(),program=path.join(out,'native-probe.cjs')
const runKey='HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'
const approvedKey='HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run'
const executable=path.resolve('tools/electron/electron.exe')
const read=key=>spawnSync('reg.exe',['query',key,'/v',name,'/reg:64'],{encoding:'utf8',windowsHide:true})
assert(read(runKey).status!==0,'Probe value unexpectedly exists')
await fs.writeFile(program,`const assert=require('node:assert/strict'),{app}=require('electron');
const {createLoginItems}=require(process.env.DANMU_TEST_LOGIN_MODULE);
app.setPath('userData',process.env.DANMU_TEST_LOGIN_PROFILE);
const options={path:process.execPath,args:['--tray','--start','示例 参数'],name:process.env.DANMU_TEST_LOGIN_NAME};
const login=createLoginItems(app,options);
app.whenReady().then(()=>{const phase=process.env.DANMU_TEST_LOGIN_PHASE,cases=[];
 try{
  if(phase==='enable'){
   assert.equal(login.get(),false);assert.equal(login.set(true),true);
   const actual=app.getLoginItemSettings(options);
   assert.equal(actual.openAtLogin,true);assert(actual.launchItems.some(i=>i.name===options.name&&i.scope==='user'&&i.enabled));
   cases.push('真实 Windows 注册后，同名 AppUserModelID 正确读到已启用状态');
   cases.push('带空格和中文参数的启动命令可正确写入并读回');
  }else{
   assert.equal(login.get(),true);cases.push('全新进程可读取上一进程写入的真实启动项');
   app.setLoginItemSettings({...options,openAtLogin:true,enabled:false});
   assert.equal(login.get(),false);cases.push('系统禁用该启动项时，界面状态为未启用');
   assert.equal(login.set(true),true);cases.push('重新开启可恢复系统启动批准状态');
   assert.equal(login.set(false),false);cases.push('取消后移除对应启动项');
  }
  console.log('NATIVE_LOGIN_RESULT:'+JSON.stringify({passed:true,phase,cases}));
 }catch(error){console.error(error.stack);app.exitCode=1}
 finally{app.exit(app.exitCode||0)}
});`)
const env={...process.env,DANMU_TEST_LOGIN_MODULE:path.resolve('electron/login-items.cjs'),DANMU_TEST_LOGIN_PROFILE:path.join(out,'profile'),DANMU_TEST_LOGIN_NAME:name};delete env.ELECTRON_RUN_AS_NODE
const report={passed:false,cases:[]}
try{
 for(const phase of ['enable','cold-verify']){
  const result=spawnSync(executable,[program],{env:{...env,DANMU_TEST_LOGIN_PHASE:phase},encoding:'utf8',windowsHide:true,timeout:30000})
  assert.equal(result.status,0,result.stderr||result.error?.message)
  const line=result.stdout.split(/\r?\n/).find(line=>line.startsWith('NATIVE_LOGIN_RESULT:'))
  assert(line,'Native process did not return a test result')
  const data=JSON.parse(line.slice('NATIVE_LOGIN_RESULT:'.length));report.cases.push(...data.cases)
  if(phase==='enable')assert.equal(read(runKey).status,0,'Native API did not write its Run value')
 }
 assert(read(runKey).status!==0,'Run value was not removed')
 assert(read(approvedKey).status!==0,'Startup approval value was not removed')
 report.passed=true
}finally{
 // These exact unique names were reserved by this test. Never touch the user's
 // actual DanmuCinema or any other application's startup registration.
 for(const key of [runKey,approvedKey])spawnSync('reg.exe',['delete',key,'/v',name,'/f','/reg:64'],{windowsHide:true,stdio:'ignore'})
 report.cleanup=read(runKey).status!==0&&read(approvedKey).status!==0
 await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2))
}
console.log(JSON.stringify(report))
