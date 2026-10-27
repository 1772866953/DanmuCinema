import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import net from 'node:net'
import {spawn} from 'node:child_process'
import {runtime} from './test-runtime.mjs'
import {randomBytes,createHash} from 'node:crypto'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/installer'),target=path.join(out,'installed'),media=path.join(out,'synthetic-media'),request=path.join(out,'private-request.json'),report={passed:false,cases:[],errors:[]}
const version=JSON.parse(await fs.readFile('package.json','utf8')).version
const setup=path.join(out,`compiler/DanmuCinema-Setup-${version}-win-x64.exe`),check=(v,label)=>{assert(v,label);report.cases.push(label)}
const execute=(file,args)=>new Promise((resolve,reject)=>{const child=spawn(file,args,{windowsHide:true,stdio:'ignore'});const timer=setTimeout(()=>{child.kill();reject(new Error('Installer operation timed out'))},240000);child.on('error',e=>{clearTimeout(timer);reject(e)});child.on('exit',code=>{clearTimeout(timer);code===0?resolve():reject(new Error(`Installer operation failed (${code})`))})})
const freePort=()=>new Promise(resolve=>{const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p))})})
const exists=async p=>fs.access(p).then(()=>true,()=>false),hash=async p=>createHash('sha256').update(await fs.readFile(p)).digest('hex')
let app
try{
 await fs.mkdir(media,{recursive:true});const video=path.join(media,'synthetic-video.mkv');await fs.writeFile(video,Buffer.alloc(128,7));const original=await hash(video),existing=process.argv.includes('--reuse-install')?JSON.parse(await fs.readFile(path.join(target,'data/settings.json'),'utf8')):null,port=existing?.Port||await freePort(),danmuPort=existing?.DanmuPort||await freePort();
 const options={AdminName:'InstallerTest',Password:randomBytes(18).toString('base64url'),MediaFolder:media,LibraryType:'mixed',Port:port,DanmuPort:danmuPort,CloseToTray:true,AutoStart:false,DandanConfigFile:''}
 async function install(){await fs.writeFile(request,JSON.stringify(options));await execute(setup,['/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',`/DIR=${target}`,`/CONFIG=${request}`,`/LOG=${path.join(out,'install.log')}`]);await fs.rm(request,{force:true})}
 console.log('Testing isolated install with synthetic media...');if(!existing)await install();
 const settings=JSON.parse(await fs.readFile(path.join(target,'data/settings.json'),'utf8')),official=JSON.parse(await fs.readFile(path.join(target,'config/dandanplay.json'),'utf8'))
 check(settings.AdminName==='InstallerTest'&&settings.MediaFolder===media&&settings.Port===port,'安装向导参数初始化真实账号、媒体目录和端口');check(!official.AppId&&!official.AppSecret&&!official.EncryptedAppSecret,'安装后官方 API 配置为空，不含个人凭据')
 const env={...process.env};delete env.ELECTRON_RUN_AS_NODE;delete env.DANMU_ELECTRON_USER_DATA
 app=await _electron.launch({executablePath:path.join(target,'DanmuCinema.exe'),args:[],env,timeout:40000});const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor({timeout:30000})
 let state;const deadline=Date.now()+90000;do{state=await page.evaluate(()=>window.cinema.call('state'));if(state.running&&state.gateway)break;await page.waitForTimeout(250)}while(Date.now()<deadline);check(state.running&&state.gateway&&state.settings.AdminName==='InstallerTest','安装后 Electron 界面与真实视频、弹幕服务共同正常运行')
 check(!await page.getByText('本地缓存优先',{exact:false}).count()&&!await page.getByText('优先读取本地缓存',{exact:false}).count(),'安装版首页移除缓存优先等介绍字样');await page.screenshot({path:path.join(out,'installed-overview.png')})
 const userData=await app.evaluate(({app})=>app.getPath('userData'));check(userData===path.join(target,'data/electron'),'安装版将窗口数据保存在本安装目录中，支持完整卸载')
 await page.evaluate(()=>window.cinema.system('quitConfirmed'));await new Promise(resolve=>app.process().once('exit',resolve));app=null
 const cache=path.join(target,'data/cache/installer-check.bin'),logs=path.join(target,'data/controller.log.keep-test');await fs.mkdir(path.dirname(cache),{recursive:true});await fs.writeFile(cache,'synthetic cache');await fs.writeFile(logs,'synthetic log')
 console.log('Testing uninstall with cache/log retention...');await execute(path.join(target,'unins000.exe'),['/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/REMOVECACHELOGS=0',`/LOG=${path.join(out,'uninstall-keep.log')}`]);check(await exists(cache)&&await exists(logs),'卸载时可保留缓存与日志');check(!await exists(path.join(target,'config/dandanplay.json'))&&!await exists(path.join(target,'data/settings.json'))&&!await exists(path.join(target,'data/electron')),'保留缓存时仍删除账号、API 和窗口配置');check(await hash(video)===original,'卸载保留缓存不修改媒体文件')
 console.log('Testing reinstall and complete app-data removal...');await install();await execute(path.join(target,'unins000.exe'),['/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/REMOVECACHELOGS=1',`/LOG=${path.join(out,'uninstall-remove.log')}`]);check(!await exists(cache)&&!await exists(logs),'卸载时也可删除全部缓存和日志');check(await hash(video)===original,'重新安装与完整卸载均不修改媒体文件');check(report.errors.length===0,'安装版页面没有 JavaScript 异常');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.rm(request,{force:true});if(app)await app.close();await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2))}
console.log(JSON.stringify(report,null,2))
