import {useEffect,useMemo,useState} from 'react'
import {FolderOpen,Files,ArrowLeftRight,ArrowLeft,ArrowRight,CheckCircle2} from 'lucide-react'
import {GlassButton as Button} from './glass'
import {SelectControl as Select} from './controls'
import {CheckBox,Field,Input,Empty} from './ui'
import {rpc,system,type Data} from './api'

type FileEntry={path:string;name:string;size:number}
const basename=(path:string)=>path.split(/[\\/]/).at(-1)||'',stem=(name:string)=>name.replace(/\.[^.]+$/,''),extension=(name:string)=>name.slice(stem(name).length)
const pageSize=25
export function FilePairingDialog(){
 const [videos,setVideos]=useState<FileEntry[]>([]),[danmu,setDanmu]=useState<FileEntry[]>([]),[videoDirectory,setVideoDirectory]=useState(''),[danmuDirectory,setDanmuDirectory]=useState('')
 const [matches,setMatches]=useState<number[]>([]),[mode,setMode]=useState('danmu'),[placeWithVideo,setPlaceWithVideo]=useState(false),[plan,setPlan]=useState<Data|null>(null)
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState(''),[page,setPage]=useState(0),[sourcesVersion,setSourcesVersion]=useState(0)
 useEffect(()=>{setMatches(danmu.map((_,i)=>i<videos.length?i:-1));setPage(0);setPlan(null)},[sourcesVersion])
 useEffect(()=>{setPlan(null)},[matches,mode,placeWithVideo])
 const options=useMemo(()=>[{value:'-1',label:'不处理'},...videos.map((file,i)=>({value:String(i),label:file.name}))],[videos])
 const edit=(change:()=>void)=>{setPlan(null);setMessage('');setError('');change()}
 const paired=matches.filter(i=>i>=0&&i<videos.length).length,pages=Math.max(1,Math.ceil(danmu.length/pageSize)),shown=danmu.slice(page*pageSize,(page+1)*pageSize)
 async function read(kind:'video'|'danmu',folder:boolean){
  setError('');setMessage('');setPlan(null);setBusy(true)
  try{
   const chosen=await system(folder?'chooseFolder':'chooseRenameFiles',kind);if(!chosen||(Array.isArray(chosen)&&!chosen.length))return
   const files=await rpc('renameFiles',{kind,...(folder?{directory:chosen}:{files:chosen})}) as FileEntry[]
   if(kind==='video'){setVideos(files);setVideoDirectory(folder?chosen:'已选择 '+files.length+' 个文件')}
   else{setDanmu(files);setDanmuDirectory(folder?chosen:'已选择 '+files.length+' 个文件')}
   setSourcesVersion(v=>v+1)
   if(!files.length)setError(folder?'所选目录的根层没有此类文件。':'未选择支持的文件。')
  }catch(e){setError((e as Error).message)}finally{setBusy(false)}
 }
 async function preview(){setBusy(true);setError('');setMessage('');setPlan(null);try{const value=await rpc('renamePreview',{videos:videos.map(x=>x.path),danmu:danmu.map(x=>x.path),matches,reverse:mode==='video',placeWithVideo});setPlan(value);if(!value.count)setMessage('所选文件已经同名，无需更改。')}catch(e){setError((e as Error).message)}finally{setBusy(false)}}
 async function apply(){
  if(!plan)return;setBusy(true);setError('')
  try{
   const count=await rpc('renameApply',{token:plan.token}),changed=new Map<string,string>(plan.changes.map((c:Data)=>[c.source.toLowerCase(),c.target]))
   const update=(files:FileEntry[])=>files.map(f=>{const path=changed.get(f.path.toLowerCase())||f.path;return {...f,path,name:basename(path)}})
   setVideos(update(videos));setDanmu(update(danmu));setPlan(null);setMessage(`已处理 ${count} 个文件。`)
  }catch(e){setError((e as Error).message);setPlan(null)}finally{setBusy(false)}
 }
 return <div className="file-pairing"><div className="pairing-content">
  <fieldset disabled={busy} className="pairing-sources">
   {(['video','danmu'] as const).map(kind=><section className="pairing-source" key={kind}><h3>{kind==='video'?'视频文件':'弹幕 / 字幕文件'}<small>{(kind==='video'?videos:danmu).length} 个</small></h3><Input readOnly aria-label={kind==='video'?'视频来源':'弹幕来源'} title={kind==='video'?videoDirectory:danmuDirectory} value={kind==='video'?videoDirectory:danmuDirectory} placeholder="选择目录或文件"/><div className="actions"><Button onClick={()=>read(kind,true)}><FolderOpen size={15}/>选择目录</Button><Button onClick={()=>read(kind,false)}><Files size={15}/>选择文件</Button></div></section>)}
  </fieldset>
  <p className="pairing-hint">只读取所选目录的根层文件，不读取子目录。</p>
  <fieldset disabled={busy} className="pairing-options"><Field label="重命名方向"><Select label="重命名方向" value={mode} disabled={busy} onChange={v=>edit(()=>setMode(v))} options={[{value:'danmu',label:'弹幕 / 字幕跟随视频'},{value:'video',label:'视频跟随弹幕 / 字幕'}]}/></Field><CheckBox checked={placeWithVideo} onChange={v=>edit(()=>setPlaceWithVideo(v))}>将配对的弹幕 / 字幕移至视频目录</CheckBox></fieldset>
  <div className="pairing-toolbar"><span>已配对 {paired} / {danmu.length}</span><div className="actions"><Button disabled={busy||!videos.length||!danmu.length} onClick={()=>edit(()=>setMatches(danmu.map((_,i)=>i<videos.length?i:-1)))}>按顺序配对</Button><Button disabled={busy||!paired} onClick={()=>edit(()=>setMatches(danmu.map(()=>-1)))}>清空配对</Button></div></div>
  <div className="pairing-table table-shell"><table><thead><tr><th>弹幕 / 字幕文件</th><th>配对视频</th><th>{mode==='video'?'视频的新名称':'弹幕 / 字幕的新名称'}</th></tr></thead><tbody>{shown.map((file,offset)=>{const i=page*pageSize+offset,video=videos[matches[i]],name=video?(mode==='video'?stem(file.name)+extension(video.name):stem(video.name)+extension(file.name)):'';return <tr key={file.path}><td title={file.path}>{file.name}</td><td><Select label={`为 ${file.name} 配对视频`} disabled={busy} value={String(matches[i]??-1)} options={options} onChange={v=>edit(()=>setMatches(old=>old.map((n,index)=>index===i?Number(v):n)))}/></td><td title={name}>{name||'—'}</td></tr>})}</tbody></table>{!danmu.length&&<Empty icon={<ArrowLeftRight size={32}/>}>选择视频与弹幕文件后，在这里核对配对关系。</Empty>}</div>
  {pages>1&&<div className="pairing-pagination"><Button className="icon small" aria-label="上一页配对" disabled={!page||busy} onClick={()=>setPage(p=>p-1)}><ArrowLeft size={14}/></Button><span>{page+1} / {pages}</span><Button className="icon small" aria-label="下一页配对" disabled={page===pages-1||busy} onClick={()=>setPage(p=>p+1)}><ArrowRight size={14}/></Button></div>}
  {!placeWithVideo&&<p className="pairing-hint">文件保留原目录；自动加载通常需要与视频位于同一目录。</p>}
  {plan&&plan.count>0&&<div className="pairing-preview"><strong><CheckCircle2 size={15}/>检查通过，将处理 {plan.count} 个文件</strong><div>{plan.changes.map((change:Data)=><p key={change.source}><span title={change.source}>{change.source}</span><ArrowRight size={13}/><span title={change.target}>{change.target}</span></p>)}</div></div>}
  {error&&<p role="alert" className="error-text pairing-result">{error}</p>}{message&&<p role="status" className="pairing-result">{message}</p>}
  </div><div className="actions modal-actions"><Button disabled={busy||!paired} onClick={preview}>{busy?'正在处理…':'检查并预览'}</Button><Button className="primary" disabled={busy||!plan?.count} onClick={apply}>执行重命名</Button></div>
 </div>
}
