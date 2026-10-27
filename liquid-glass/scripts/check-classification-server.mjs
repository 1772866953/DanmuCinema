import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import net from 'node:net'
import {spawn} from 'node:child_process'
import {randomBytes,createHash} from 'node:crypto'
const removalOnly=process.argv.includes('--removal-only')
const out=path.resolve(removalOnly?'artifacts/library-removal-server':'artifacts/classification-server'),root=path.join(out,'fixture-'+Date.now()),data=path.join(root,'server'),media=path.join(root,'media'),runtime=path.resolve('runtime/jellyfin')
const report={passed:false,cases:[],errors:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
const delay=ms=>new Promise(r=>setTimeout(r,ms)),freePort=()=>new Promise(resolve=>{const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p))})})
const run=(exe,args)=>new Promise((resolve,reject)=>{const c=spawn(exe,args,{windowsHide:true,stdio:'ignore'});c.on('error',reject);c.on('exit',code=>code===0?resolve():reject(new Error('Fixture process failed: '+code)))})
await fs.mkdir(out,{recursive:true});await fs.mkdir(path.join(data,'config'),{recursive:true});const port=await freePort(),base=`http://127.0.0.1:${port}`
await fs.writeFile(path.join(data,'config/network.xml'),`<NetworkConfiguration><InternalHttpPort>${port}</InternalHttpPort><PublicHttpPort>${port}</PublicHttpPort><EnableIPv4>true</EnableIPv4><EnableIPv6>false</EnableIPv6><EnableAutoDiscovery>false</EnableAutoDiscovery><EnableUPnP>false</EnableUPnP><EnableRemoteAccess>false</EnableRemoteAccess></NetworkConfiguration>`)
await fs.cp(path.resolve('development/bin/playback-plugin'),path.join(data,'plugins/DanmuCinemaPlayback_1.1.0.0'),{recursive:true})
await fs.mkdir(media,{recursive:true});const sample=path.join(root,'sample.mp4');await run(path.join(runtime,'ffmpeg.exe'),['-hide_banner','-loglevel','error','-f','lavfi','-i','color=c=black:s=64x64:r=1','-t','1','-c:v','libx264','-pix_fmt','yuv420p',sample])
for(const name of ['分类动画 第一季','爱情公寓1']){await fs.mkdir(path.join(media,name));for(let i=1;i<=2;i++)await fs.copyFile(sample,path.join(media,name,`Synthetic S01E0${i}.mp4`))}
const snapshots=async()=>{const rows={};for(const dir of await fs.readdir(media)){for(const file of await fs.readdir(path.join(media,dir))){const relative=path.join(dir,file);rows[relative]=createHash('sha256').update(await fs.readFile(path.join(media,relative))).digest('hex')}}return rows}
if(removalOnly)for(const dir of await fs.readdir(media))for(const suffix of ['srt','xml'])await fs.writeFile(path.join(media,dir,'Synthetic S01E01.'+suffix),suffix==='xml'?'<i/>':'1\n00:00:00,000 --> 00:00:01,000\nSynthetic subtitle\n')
const original=await snapshots(),log=await fs.open(path.join(root,'server.log'),'a')
const server=spawn(path.join(runtime,'jellyfin.exe'),['--datadir',data,'--cachedir',path.join(root,'cache'),'--logdir',path.join(root,'logs'),'--webdir',path.join(runtime,'jellyfin-web'),'--ffmpeg',path.join(runtime,'ffmpeg.exe')],{cwd:runtime,windowsHide:true,stdio:['ignore',log.fd,log.fd]})
let token=''
async function request(route,method='GET',body,auth=true){const r=await fetch(base+'/'+route,{method,headers:{Authorization:'MediaBrowser Client="DanmuCinemaTest", Device="Windows", DeviceId="classification-fixture", Version="1.0.0"'+(auth&&token?`, Token="${token}"`:''),...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&token?{'X-Emby-Token':token}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});const text=await r.text();assert(r.ok,`${route.split('?')[0]} returned ${r.status}`);return text?JSON.parse(text):null}
try {
 for(let i=0;i<100;i++){if(server.exitCode!==null)throw new Error('Isolated server exited');try{const health=await fetch(base+'/health',{signal:AbortSignal.timeout(2000)});assert(health.ok&&(await health.text()).trim()==='Healthy');break}catch(e){if(i===99)throw e;await delay(400)}}
 await request('Startup/Configuration','POST',{ServerName:'Classification fixture',UICulture:'zh-CN',MetadataCountryCode:'CN',PreferredMetadataLanguage:'zh'},false)
 await request('Startup/User','GET',undefined,false);const password=randomBytes(20).toString('base64url');await request('Startup/User','POST',{Name:'ClassificationTest',Password:password},false)
 await request('Startup/RemoteAccess','POST',{EnableRemoteAccess:false,EnableAutomaticPortMapping:false},false);await request('Startup/Complete','POST',{},false)
 const login=await request('Users/AuthenticateByName','POST',{Username:'ClassificationTest',Pw:password},false);token=login.AccessToken;check(Boolean(token),'隔离服务器完成测试管理员初始化')
 if(removalOnly){
  const idle=async()=>{for(let i=0;i<100;i++){const tasks=await request('ScheduledTasks');if(!tasks.some(t=>t.Key==='RefreshLibrary'&&['Running','Cancelling'].includes(t.State)))return;await delay(250)}throw new Error('Scan did not become idle')}
  const scan=async()=>{await idle();const task=(await request('ScheduledTasks')).find(t=>t.Key==='RefreshLibrary'),before=JSON.stringify(task.LastExecutionResult);await request('ScheduledTasks/Running/'+task.Id,'POST',{});for(let i=0;i<120;i++){const now=await request('ScheduledTasks/'+task.Id);if(!['Running','Cancelling'].includes(now.State)&&JSON.stringify(now.LastExecutionResult)!==before)return;await delay(250)}throw new Error('Scan did not finish')}
  await idle();const dirs=await fs.readdir(media),names=['片库 A & 删除测试','保留的片库 B'];
  for(let i=0;i<2;i++)await request('Library/VirtualFolders?name='+encodeURIComponent(names[i])+'&collectionType=movies&refreshLibrary=false','POST',{LibraryOptions:{PathInfos:[{Path:path.join(media,dirs[i])}],SaveLocalMetadata:false,EnableRealtimeMonitor:false,TypeOptions:[{Type:'Movie',MetadataFetchers:[],ImageFetchers:[]}]}})
  let all=[];for(let i=0;i<3;i++){await scan();all=(await request('Items?Recursive=true&IncludeItemTypes=Movie,Episode,Video&Fields=Path')).Items;if(all.length===4)break}
  check(all.length===4,'两个隔离媒体库均完成真实索引')
  const denied=await fetch(base+'/Library/VirtualFolders?name='+encodeURIComponent(names[0])+'&refreshLibrary=false',{method:'DELETE'});check(denied.status===401,'匿名调用不能删除媒体库')
  await request('Library/VirtualFolders?name='+encodeURIComponent(names[0])+'&refreshLibrary=false','DELETE')
  let libs=await request('Library/VirtualFolders');check(libs.length===1&&libs[0].Name===names[1],'仅移除确认的媒体库记录，特殊名称可正确处理')
  await scan();const remaining=(await request('Items?Recursive=true&IncludeItemTypes=Movie,Episode,Video&Fields=Path')).Items
  check(remaining.length===2&&remaining.every(item=>item.Path.startsWith(path.join(media,dirs[1])+path.sep)),'真实扫描后移除旧索引并保留另一个媒体库影片')
  check(JSON.stringify(await snapshots())===JSON.stringify(original),'删除单个媒体库后视频、字幕、XML 内容均不变')
  await request('Library/VirtualFolders?name='+encodeURIComponent(names[1])+'&refreshLibrary=false','DELETE');await scan();libs=await request('Library/VirtualFolders')
  check(libs.length===0&&(await request('Items?Recursive=true&IncludeItemTypes=Movie,Episode,Video')).Items.length===0,'真实服务器可删除最后一库并清空对应索引')
  check(JSON.stringify(await snapshots())===JSON.stringify(original),'删除全部媒体库后两个原目录及全部媒体文件仍保留')
 }else{
 const types=['Series','Season','Episode','Movie','Video'].map(Type=>({Type,MetadataFetchers:[],ImageFetchers:[]}))
 for(let i=0;i<100;i++){const tasks=await request('ScheduledTasks');if(!tasks.some(t=>t.Key==='RefreshLibrary'&&['Running','Cancelling'].includes(t.State)))break;await delay(250)}
 await request('Library/VirtualFolders?name=ClassificationFixture&refreshLibrary=true','POST',{LibraryOptions:{PathInfos:[{Path:media}],SaveLocalMetadata:false,EnableRealtimeMonitor:false,TypeOptions:types}})
 for(let i=0;i<100;i++){const tasks=await request('ScheduledTasks');if(!tasks.some(t=>t.Key==='RefreshLibrary'&&['Running','Cancelling'].includes(t.State)))break;await delay(250)}
 await request('Library/Refresh','POST',{})
 let items=[];for(let i=0;i<100;i++){const result=await request('Items?Recursive=true&IncludeItemTypes=Episode&Fields=Path,Genres,Tags,ProviderIds');items=result.Items||[];if(items.length===4)break;await delay(400)}
 check(items.length===4,'自动媒体库识别两个作品的完整测试剧集')
 const anime=items.filter(x=>x.Path.includes('分类动画')),tv=items.filter(x=>x.Path.includes('爱情公寓'))
 const details=async id=>(await request('Items?Ids='+id+'&Fields=Path,Genres,Tags,ProviderIds')).Items[0]
 const episode=await details(anime[0].Id),series=await details(episode.SeriesId);series.Genres=['剧情'];series.Tags=['原有标签'];await request('Items/'+series.Id,'POST',series)
 const denied=await fetch(base+'/DanmuCinema/Classification',{method:'POST',headers:{'Content-Type':'application/json'},body:'{"Items":[]}'});check(denied.status===401,'分类写入接口拒绝匿名调用')
 const payload={Items:items.map(x=>({Id:x.Id,Path:x.Path,Anime:x.Path.includes('分类动画')}))}
 const first=await request('DanmuCinema/Classification','POST',payload);check(first.Skipped===0&&first.Updated>=3,'播放准备组件完成分类同步')
 const now=await details(anime[0].Id),parent=await details(now.SeriesId),live=await details(tv[0].Id)
 check(now.Genres.includes('Animation')&&now.Tags.includes('动漫'),'客户端剧集接口返回动漫类型和标签')
 check(parent.Genres.includes('动漫')&&parent.Genres.includes('剧情')&&parent.Tags.includes('原有标签'),'作品级动漫分类保留原有类型和标签')
 check(!live.Genres.includes('Animation')&&!live.Tags.includes('DanmuCinema:Anime'),'爱情公寓不被归入动漫')
 check(now.Id===episode.Id&&now.Name===episode.Name&&now.IndexNumber===episode.IndexNumber&&now.SeriesId===episode.SeriesId,'同步前后影片编号、名称和季集结构保持一致')
 const filtered=await request('Items?Recursive=true&IncludeItemTypes=Series&Genres=Animation&Fields=Genres,Tags');check(filtered.Items.length===1&&filtered.Items[0].Id===parent.Id,'客户端可按动漫类型筛选正确作品')
 const repeated=await request('DanmuCinema/Classification','POST',payload);check(repeated.Updated===0,'重复同步不重复写入数据库')
 const wrong=await request('DanmuCinema/Classification','POST',{Items:[{Id:anime[0].Id,Path:'C:\\unrelated.mp4',Anime:false}]});check(wrong.Skipped===1&&(await details(parent.Id)).Genres.includes('Animation'),'来源路径不一致时拒绝修改分类')
 await request('DanmuCinema/Classification','POST',{Items:anime.map(x=>({Id:x.Id,Path:x.Path,Anime:false}))});const restored=await details(parent.Id)
 check(JSON.stringify(restored.Genres)==='["剧情"]'&&JSON.stringify(restored.Tags)==='["原有标签"]','重新分类只移除程序添加的标签，保留原有元数据')
 check(JSON.stringify(await snapshots())===JSON.stringify(original),'客户端分类同步未改动视频或在媒体目录新增任何文件')
 }
 report.passed=true
} catch(e){report.errors.push(e.stack);throw e} finally {
 try{if(token)await request('System/Shutdown','POST',{})}catch{}
 for(let i=0;i<30&&server.exitCode===null;i++)await delay(100)
 if(server.exitCode===null){server.kill();await new Promise(r=>server.once('exit',r))}
 await log.close();await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2))
}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,checks:report.cases},null,2))
