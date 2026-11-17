import {useMediaActivation} from './media-activation'
import {useEffect,useRef,useSyncExternalStore} from 'react'
import {Folder,Film,Play,MessagesSquare} from 'lucide-react'
import {GlassButton as Button} from './glass'
import {CheckBox} from './ui'
import {thumbnails,thumbnailKey,decodeThumbnail} from './media-cache'
import {rpc,type Data} from './api'
import {ArrowUp,ChevronsUpDown} from 'lucide-react'
export function CoverSortBar({sort,descending,onSort,details}:{sort:number;descending:boolean;onSort(value:number):void;details:boolean}){
 const fields:[string,number][]=[['名称',0],['类型',3],['大小',2],['修改时间',1],['弹幕',5],...(details?[['码率',4] as [string,number]]:[])]
 return <div className="cover-sorting" role="group" aria-label="封面排序"><span>排序</span>{fields.map(([label,key])=><Button key={key} className={`cover-sort-button ${sort===key?'active':''}`} aria-label={`${label}排序`} aria-pressed={sort===key} title={sort===key?`当前${descending?'降序':'升序'}，点击切换为${descending?'升序':'降序'}`:`按${label}升序排列`} onClick={()=>onSort(key)}>{label}{sort===key?<ArrowUp size={12} style={{transform:descending?'rotate(180deg)':'none'}}/>:<ChevronsUpDown size={12}/>}</Button>)}</div>
}
export function mediaFileDetails(row:Data){
 const lines=[`名称：${row.name||row.filename||''}`]
 if(row.filename&&row.filename!==row.name)lines.push(`文件：${row.filename}`)
 if(row.path)lines.push(`位置：${row.path}`)
 if(row.folder)lines.push(`影片：${row.count||0} 集`)
 else if(row.type)lines.push(`类型：${row.type}`)
 if(Number.isFinite(row.size))lines.push(`大小：${row.size.toLocaleString('zh-CN',{maximumFractionDigits:2})} MB`)
 if(row.modified)lines.push(`修改时间：${row.modified}`)
 if(row.danmu)lines.push(`弹幕：${row.danmu}`)
 return lines.join('\n')
}
export function MediaCover({row}:{row:Data}){
 const host=useRef<HTMLSpanElement>(null),key=thumbnailKey(row)
 const generation=useSyncExternalStore(thumbnails.subscribe,thumbnails.epoch),url=useSyncExternalStore(thumbnails.subscribe,()=>thumbnails.read(key))
 useEffect(()=>{const element=host.current;if(!element||row.warmup)return;const observer=new IntersectionObserver(entries=>{if(entries.some(entry=>entry.isIntersecting)){void thumbnails.ensure(key,()=>rpc('cover',{key:row.coverKey||row.keys[0],series:!!row.folder}),decodeThumbnail);observer.disconnect()}},{root:element.closest('.media-gallery,.media-table'),rootMargin:'160px'});observer.observe(element);return()=>observer.disconnect()},[key,generation])
 return <span ref={host} className="media-cover">{url?<img src={url} decoding="sync" draggable={false} alt=""/>:row.folder?<Folder size={36}/>:<Film size={32}/>}</span>
}
export function PosterGrid({rows,zoom,onZoom,onOpen,onSelect,onPlay,onMatch,onContext,doubleClickMs}:{rows:Data[];doubleClickMs:number;zoom:number;onZoom(value:number):void;onOpen(row:Data):void;onSelect(row:Data,value:boolean):void;onPlay(row:Data):void;onMatch(row:Data):void;onContext(event:React.MouseEvent,row:Data):void}){
 const host=useRef<HTMLDivElement>(null),latest=useRef({zoom,onZoom});latest.current={zoom,onZoom}
 useEffect(()=>{const node=host.current;if(!node)return;const wheel=(e:WheelEvent)=>{if(!e.ctrlKey)return;e.preventDefault();const multiplier=e.deltaMode===1?16:e.deltaMode===2?node.clientHeight:1;latest.current.onZoom(Math.max(.35,Math.min(3,latest.current.zoom*Math.exp(-e.deltaY*multiplier*.0015))))};node.addEventListener('wheel',wheel,{passive:false});return()=>{node.removeEventListener('wheel',wheel)}},[])
 const activation=useMediaActivation(doubleClickMs,rows.map(r=>r.id).join('|'))
 const width=Math.max(140,Math.min(560,240*zoom))
 return <div ref={host} className="media-gallery" aria-label="媒体封面"><div className="poster-grid" style={{gridTemplateColumns:`repeat(auto-fill, minmax(min(${width}px, 100%), 1fr))`}}>{rows.map((row,index)=><div className="poster-item" key={row.id}><article data-row={index} title={mediaFileDetails(row)} className={`poster-card ${row.selected?'row-selected':''} ${row.folder?'folder-poster':'episode-poster'}`} onDoubleClick={e=>activation.double(e,()=>row.folder?onOpen(row):onPlay(row))} onContextMenu={e=>onContext(e,row)}>
  <button className="poster-image-button" aria-label={row.folder?`打开 ${row.name}`:`播放 ${row.name}`} onClick={e=>{if(e.detail===0)row.folder?onOpen(row):onPlay(row)}}><MediaCover row={row}/></button>
  <div className="poster-check"><CheckBox checked={row.selected} mixed={row.mixed} onChange={value=>onSelect(row,value)}/></div>
  <span className="poster-count">{row.folder?`${row.count} 集`:row.type||'影片'}</span>
  <div className="poster-caption"><div className="poster-description"><strong>{row.name}</strong><span>{row.danmu}</span></div><div className="poster-actions"><Button className="icon small" aria-label={`匹配 ${row.name}`} title="选择弹幕" onClick={()=>onMatch(row)}><MessagesSquare size={14}/></Button><Button className="icon small" aria-label={`播放 ${row.name}`} title="播放" onClick={()=>onPlay(row)}><Play size={14}/></Button></div></div>
 </article></div>)}</div></div>
}
