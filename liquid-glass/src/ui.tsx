import {useContext,useEffect,useRef,useState,type ReactNode,type InputHTMLAttributes} from 'react'
import {AnimatePresence,motion,useIsPresent,useMotionValue} from 'motion/react'
import LiquidGlass from 'liquid-glass-react'
import {X,Check,Search,ChevronDown,History,Trash2} from 'lucide-react'
import {AppearanceContext,GlassButton} from './glass'
import {rpc} from './api'

// The library draws only the optical layer. Foreground never enters its filter.
export function Panel({children,className='',optical=false}:{children:ReactNode;className?:string;optical?:boolean}){
 return <section className={`panel ${className}`}>
 {optical&&<div className="library-optics" aria-hidden="true"><LiquidGlass displacementScale={10} blurAmount={0} saturation={110} aberrationIntensity={.15} elasticity={0} cornerRadius={26} padding="0" overLight={false} mode="standard" globalMousePos={{x:0,y:0}} mouseOffset={{x:0,y:0}}><span/></LiquidGlass></div>}
 <div className="panel-content">{children}</div></section>
}
export function Field({label,children,hint}:{label:string;children:ReactNode;hint?:string}){return <label className="field"><span>{label}</span>{children}{hint&&<small>{hint}</small>}</label>}
export function Input(props:InputHTMLAttributes<HTMLInputElement>){return <input {...props} className={`input ${props.className||''}`}/>}
export function CheckBox({checked,mixed=false,onChange,children,disabled=false}:{checked:boolean;mixed?:boolean;onChange(value:boolean):void;children?:ReactNode;disabled?:boolean}){
 const ref=useRef<HTMLInputElement>(null),{reduced}=useContext(AppearanceContext),active=checked||mixed
 useEffect(()=>{if(ref.current)ref.current.indeterminate=mixed},[mixed])
 return <label className={`check ${disabled?'disabled':''}`} onClick={e=>e.stopPropagation()}><input ref={ref} type="checkbox" checked={checked} onChange={e=>onChange(e.target.checked)} disabled={disabled}/><span className="check-box" aria-hidden="true">
 <motion.span className="check-glass" initial={false} animate={{scale:reduced?1:active?[1,.88,1.07,1]:[1,.92,1]}} transition={{duration:reduced?0:.32}}/>
 <motion.svg className="check-glyph" viewBox="0 0 16 16" initial={false} animate={{opacity:active?1:0}} transition={{duration:reduced?.05:.12}}><motion.path d={mixed?'M4 8H12':'M3.5 8L6.5 11L12.5 4.5'} fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" initial={false} animate={{pathLength:active?1:0}} transition={{duration:reduced?.05:.22,ease:[.22,1,.36,1]}}/></motion.svg>
 </span>{children&&<span>{children}</span>}</label>
}
export function Empty({icon,children}:{icon?:ReactNode;children:ReactNode}){return <div className="empty">{icon}<p>{children}</p></div>}
export function SearchBox({scope,value,onChange,onSubmit,placeholder='搜索当前媒体库'}:{scope:string;value:string;onChange(value:string):void;onSubmit?(value:string):void;placeholder?:string}){
 const [open,setOpen]=useState(false),[history,setHistory]=useState<string[]>([]);const host=useRef<HTMLDivElement>(null)
 const refresh=()=>rpc('history',{scope}).then(setHistory).catch(()=>{})
 useEffect(()=>{if(open)refresh()},[open,scope])
 useEffect(()=>{if(scope!=='library'||!value.trim())return;const timer=setTimeout(()=>rpc('historyAdd',{scope,text:value}).catch(()=>{}),900);return()=>clearTimeout(timer)},[scope,value])
 useEffect(()=>{const fn=(e:PointerEvent)=>{if(!host.current?.contains(e.target as Node))setOpen(false)};document.addEventListener('pointerdown',fn);return()=>document.removeEventListener('pointerdown',fn)},[])
 return <div className="search-wrap" ref={host}><Search size={17}/><input aria-label={placeholder} value={value} placeholder={placeholder} onFocus={()=>setOpen(true)} onChange={e=>onChange(e.target.value)} onKeyDown={e=>{if(e.key==='Enter'){rpc('historyAdd',{scope,text:value}).catch(()=>{});setOpen(false);onSubmit?.(value)}else if(e.key==='Escape'){e.stopPropagation();setOpen(false)}}}/><>{value&&<GlassButton className="icon small" title="清除筛选文字" aria-label="清除筛选文字" onClick={()=>onChange('')}><X size={13}/></GlassButton>}</><GlassButton className="icon small" title="搜索历史" aria-label="搜索历史" onClick={()=>setOpen(!open)}><ChevronDown size={15}/></GlassButton>
 <AnimatePresence>{open&&history.length>0&&<motion.div className="history-menu" initial={{opacity:0,y:-5}} animate={{opacity:1,y:0}} exit={{opacity:0,y:-3}} transition={{duration:.14}}><header><span><History size={13}/>搜索历史</span><GlassButton className="text-button" onClick={async()=>{await rpc('historyClear',{scope});refresh()}}>清空历史</GlassButton></header>{history.slice(0,8).map(text=><div className="history-row" key={text}><button onClick={()=>{onChange(text);setOpen(false);onSubmit?.(text)}}>{text}</button><GlassButton className="icon small" title="删除这条历史" aria-label={`删除历史 ${text}`} onClick={async()=>{await rpc('historyRemove',{scope,text});refresh()}}><X size={13}/></GlassButton></div>)}</motion.div>}</AnimatePresence></div>
}
export function Modal({title,children,onClose,top=true,wide=false}:{title:string;children:ReactNode;onClose():void;top?:boolean;wide?:boolean}){
 const {reduced}=useContext(AppearanceContext),present=useIsPresent(),ref=useRef<HTMLDivElement>(null),previous=useRef<HTMLElement|null>(null),x=useMotionValue(0),y=useMotionValue(0),drag=useRef<{id:number;x:number;y:number;ox:number;oy:number}|null>(null)
 useEffect(()=>{previous.current=document.activeElement as HTMLElement;const timer=setTimeout(()=>ref.current?.focus(),0);return()=>{clearTimeout(timer);previous.current?.isConnected&&previous.current.focus({preventScroll:true})}},[])
 useEffect(()=>{if(!top)return;const fn=(e:KeyboardEvent)=>{if(e.defaultPrevented)return;if(e.key==='Escape'){e.preventDefault();onClose()}if(e.key==='Tab'){const items=Array.from(ref.current?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),textarea,select,[tabindex="0"]')||[]).filter(el=>el.getClientRects().length);if(!items.length){e.preventDefault();ref.current?.focus();return}const i=items.indexOf(document.activeElement as HTMLElement);if(e.shiftKey&&i<=0){e.preventDefault();items.at(-1)?.focus()}else if(!e.shiftKey&&(i===items.length-1||i<0)){e.preventDefault();items[0]?.focus()}}};window.addEventListener('keydown',fn);return()=>window.removeEventListener('keydown',fn)},[top,onClose])
 return <div className={`modal-layer ${top?'':'under-modal'}`} inert={!present||!top||undefined} style={{pointerEvents:present?'auto':'none'}}>
 <motion.div className="modal-scrim" initial={{opacity:0}} animate={{opacity:1}} exit={{opacity:0}} transition={{duration:reduced?.07:.2}} onPointerDown={e=>{if(e.target===e.currentTarget&&top)onClose()}}/>
 <motion.div className={`modal-position ${wide?'wide':''}`} style={{x,y}}><motion.div className="modal-presentation" ref={ref} role="dialog" aria-modal={top} aria-label={title} tabIndex={-1}
 initial={{opacity:0}} animate={{opacity:1}} exit={{opacity:0,transition:{duration:reduced?.07:.2,ease:[.4,0,.2,1]}}} transition={{duration:reduced?.07:.2,ease:[.22,1,.36,1]}}
 ><header className="modal-header" onPointerDown={e=>{if((e.target as HTMLElement).closest('button')||e.button!==0)return;drag.current={id:e.pointerId,x:e.clientX,y:e.clientY,ox:x.get(),oy:y.get()};e.currentTarget.setPointerCapture(e.pointerId)}} onPointerMove={e=>{const d=drag.current;if(d?.id===e.pointerId){x.set(Math.max(-innerWidth/3,Math.min(innerWidth/3,d.ox+e.clientX-d.x)));y.set(Math.max(-innerHeight/3,Math.min(innerHeight/3,d.oy+e.clientY-d.y)))}}} onPointerUp={()=>{drag.current=null}} onPointerCancel={()=>{drag.current=null}} onLostPointerCapture={()=>{drag.current=null}}><div><span className="eyebrow">DANMUCINEMA</span><h2>{title}</h2></div><GlassButton className="icon" title="关闭此页面" aria-label="关闭此页面" onClick={onClose}><X size={18}/></GlassButton></header><div className="modal-body">{children}</div></motion.div></motion.div></div>
}
