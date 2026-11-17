import {createContext,useId,useContext,useEffect,useLayoutEffect,useRef,useState,type ReactNode,type InputHTMLAttributes} from 'react'
import {animate,AnimatePresence,motion,usePresence,useMotionValue} from 'motion/react'
import {X,Check,Search,ChevronDown,History,Trash2,Minus,Plus} from 'lucide-react'
import {AppearanceContext,GlassBackdropContext,GlassButton} from './glass'
import {rpc} from './api'
import {AmbientPanelOptics} from './panel-optics'
import {animateNativeDialog} from './dialog-animation'
const nativeDialogAnimations=import.meta.env.VITE_DANMU_NATIVE_DIALOG_ANIMATIONS!=='0'

// The optical card uses a bounded ambient input; its foreground stays native.
export function Panel({children,className='',optical=false}:{children:ReactNode;className?:string;optical?:boolean}){
 return <section className={`panel ${className}`}>
 {optical&&<AmbientPanelOptics/>}
 <div className="panel-content">{children}</div></section>
}
const FieldLabelContext=createContext({id:'',label:''})
export function Field({label,children,hint}:{label:string;children:ReactNode;hint?:string}){const id=useId();return <label className="field"><span id={id}>{label}</span><FieldLabelContext.Provider value={{id,label}}>{children}</FieldLabelContext.Provider>{hint&&<small>{hint}</small>}</label>}
export function Input(props:InputHTMLAttributes<HTMLInputElement>){
 const ref=useRef<HTMLInputElement>(null),field=useContext(FieldLabelContext)
 if(props.type!=='number')return <input {...props} className={`input ${props.className||''}`}/>
 const value=Number(props.value??props.defaultValue),empty=props.value===''||props.value===undefined&&props.defaultValue===undefined
 const unavailable=props.disabled||props.readOnly
 const step=(direction:number)=>{const node=ref.current;if(!node||unavailable)return;node.focus({preventScroll:true});const previous=node.value;try{node.stepUp(direction)}catch{return}if(node.value!==previous)node.dispatchEvent(new Event('input',{bubbles:true}))}
 return <span className={`input number-field ${props.className||''}`} style={props.style} data-disabled={props.disabled||undefined}>
  <input {...props} aria-labelledby={props['aria-label']?undefined:props['aria-labelledby']||field.id||undefined} ref={ref} style={undefined} className="number-input"/>
  <span className="number-stepper"><GlassButton className="number-step" optics="inherited" tabIndex={-1} title="减少数值" aria-label={`${props['aria-label']||'数值'}减少`} disabled={unavailable||!empty&&Number.isFinite(value)&&props.min!==undefined&&value<=Number(props.min)} onPointerDown={e=>e.preventDefault()} onClick={e=>{e.preventDefault();e.stopPropagation();step(-1)}}><Minus size={12}/></GlassButton><GlassButton className="number-step" optics="inherited" tabIndex={-1} title="增加数值" aria-label={`${props['aria-label']||'数值'}增加`} disabled={unavailable||!empty&&Number.isFinite(value)&&props.max!==undefined&&value>=Number(props.max)} onPointerDown={e=>e.preventDefault()} onClick={e=>{e.preventDefault();e.stopPropagation();step(1)}}><Plus size={12}/></GlassButton></span>
 </span>
}
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
export function Modal({title,children,onClose,top=true,wide=false,warmup=false,onEntered,retained=false,retainedName='player',visible=true,instantHide=false,order}:{title:string;children:ReactNode;onClose():void;top?:boolean;wide?:boolean;warmup?:boolean;onEntered?():void;retained?:boolean;retainedName?:string;visible?:boolean;instantHide?:boolean;order?:number}){
 const {reduced}=useContext(AppearanceContext),[presence,safeToRemove]=usePresence(),ref=useRef<HTMLDivElement>(null),previous=useRef<HTMLElement|null>(null),x=useMotionValue(0),y=useMotionValue(0),drag=useRef<{id:number;x:number;y:number;ox:number;oy:number}|null>(null),present=retained?visible:presence
 const [parked,setParked]=useState(retained&&!visible)
 const scrim=useRef<HTMLDivElement>(null)
 const scale=useMotionValue(reduced?1:.94),offset=useMotionValue(reduced?0:16),opacity=useMotionValue(0),shade=useMotionValue(0)
 const [initialHeight,setInitialHeight]=useState<number>()
 const entered=useRef(onEntered);entered.current=onEntered
 // Match TingYe's fixed layout and transform animation; no animated layout zoom
 // or ResizeObserver-to-margin feedback while content and scrollbars change.
 useLayoutEffect(()=>{if(ref.current)setInitialHeight(ref.current.offsetHeight)},[])
 useLayoutEffect(()=>{if(!retained||!present)return;if(parked){ref.current?.querySelector('.modal-body')?.scrollTo(0,0);x.set(0);y.set(0);scale.set(reduced?1:.94);offset.set(reduced?0:16);opacity.set(0);shade.set(0);if(nativeDialogAnimations&&ref.current&&scrim.current){Object.assign(ref.current.style,{transform:`translateY(${reduced?0:16}px) scale(${reduced?1:.94})`,opacity:'0'});scrim.current.style.opacity='0'}}setParked(false)},[present,retained])
 useLayoutEffect(()=>{
  if(warmup||retained&&!present)return
  const node=ref.current;previous.current=document.activeElement as HTMLElement;node?.focus({preventScroll:true})
  return()=>{
   const origin=previous.current
   // The parent loses inert during the same React commit. Restore only after
   // those mutations; do not steal focus from a newly opened sibling/reopen.
   queueMicrotask(()=>{
    if(node?.isConnected&&!node.closest('[inert]'))return
    const active=document.activeElement as HTMLElement|null
    if(active&&active!==document.body&&active!==document.documentElement&&!node?.contains(active)&&!active.closest('[inert]'))return
    const parent=Array.from(document.querySelectorAll<HTMLElement>('.modal-layer:not([inert]) .modal-presentation[role=dialog]')).at(-1)
    const target=origin&&origin!==document.body&&origin!==document.documentElement&&origin.isConnected&&!origin.closest('[inert]')?origin:parent
    target?.focus({preventScroll:true})
   })
  }
 },[warmup,retained?present:true])
 useEffect(()=>{
  let live=true
  if(nativeDialogAnimations&&ref.current&&scrim.current){
   const animation=animateNativeDialog(ref.current,scrim.current,{present,reduced,retained,instantHide,prepare:import.meta.env.VITE_DANMU_DIALOG_PAINT_READY!=='0',paintReady:()=>window.cinema.window('primeDialog')})
   animation.finished.then(()=>{if(!live)return;if(present)entered.current?.();else if(retained)setParked(true);else safeToRemove?.()}).catch(()=>{})
   return()=>{live=false;animation.cancel()}
  }
  if(retained&&!present&&instantHide){scale.set(.96);offset.set(12);opacity.set(.001);shade.set(0);setParked(true);return}
  const timing=reduced?{duration:.07}:present?{type:'spring' as const,stiffness:390,damping:34,mass:.8}:{duration:.2,ease:[.4,0,.2,1] as const}
  const controls=[animate(scale,reduced?1:present?1:.96,timing),animate(offset,reduced?0:present?0:12,timing),animate(opacity,present?1:retained?.001:0,timing),animate(shade,present?1:0,{duration:reduced?.07:.2})]
  if(!present)Promise.all(controls).then(()=>{if(live){if(retained)setParked(true);else safeToRemove?.()}})
  else if(entered.current)Promise.all(controls).then(()=>{if(live)entered.current?.()})
  return()=>{live=false;controls.forEach(control=>control.stop())}
 },[present,reduced,retained,instantHide])
 useEffect(()=>{if(warmup||!top||!present)return;const fn=(e:KeyboardEvent)=>{if(e.defaultPrevented)return;if(e.key==='Escape'){e.preventDefault();e.stopPropagation();onClose()}if(e.key==='Tab'){const items=Array.from(ref.current?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),textarea,select,[tabindex="0"]')||[]).filter(el=>el.getClientRects().length);if(!items.length){e.preventDefault();ref.current?.focus({preventScroll:true});return}const i=items.indexOf(document.activeElement as HTMLElement);if(e.shiftKey&&i<=0){e.preventDefault();items.at(-1)?.focus()}else if(!e.shiftKey&&(i===items.length-1||i<0)){e.preventDefault();items[0]?.focus()}}};window.addEventListener('keydown',fn);return()=>window.removeEventListener('keydown',fn)},[top,present,onClose,warmup])
 const contents=<><header className="modal-header" onPointerDown={e=>{if((e.target as HTMLElement).closest('button')||e.button!==0)return;drag.current={id:e.pointerId,x:e.clientX,y:e.clientY,ox:x.get(),oy:y.get()};e.currentTarget.setPointerCapture(e.pointerId)}} onPointerMove={e=>{const d=drag.current;if(d?.id===e.pointerId){x.set(Math.max(-innerWidth/3,Math.min(innerWidth/3,d.ox+e.clientX-d.x)));y.set(Math.max(-innerHeight/3,Math.min(innerHeight/3,d.oy+e.clientY-d.y)))}}} onPointerUp={()=>{drag.current=null}} onPointerCancel={()=>{drag.current=null}} onLostPointerCapture={()=>{drag.current=null}}><div><span className="eyebrow">DANMUCINEMA</span><h2>{title}</h2></div><GlassButton className="icon" title="关闭此页面" aria-label="关闭此页面" onClick={onClose}><X size={18}/></GlassButton></header><div className="modal-body">{children}</div></>
 return <div className={`modal-layer ${top?'':'under-modal'}`} data-startup-warmup={warmup?'player-form':undefined} data-retained-dialog={retained?retainedName:undefined} data-parked={retained&&parked&&!present?true:undefined} aria-hidden={warmup||retained&&!present||undefined} inert={warmup||!present||!top||undefined} style={{pointerEvents:present&&!warmup?'auto':'none',zIndex:order===undefined?undefined:200+order}}>
 {nativeDialogAnimations?<div ref={scrim} className="modal-scrim" style={{opacity:0}} onPointerDown={e=>{if(e.target===e.currentTarget&&top&&present)onClose()}}/>:<motion.div ref={scrim} className="modal-scrim" style={{opacity:shade}} onPointerDown={e=>{if(e.target===e.currentTarget&&top&&present)onClose()}}/>}
 <motion.div className={`modal-position ${wide?'wide':''}`} style={{x,y}}><GlassBackdropContext.Provider value={true}>
 {nativeDialogAnimations?<div className="modal-presentation" ref={ref} role="dialog" aria-modal={top} aria-label={title} tabIndex={-1} style={{height:initialHeight,opacity:0,transform:`translateY(${reduced?0:16}px) scale(${reduced?1:.94})`}}>{contents}</div>:<motion.div className="modal-presentation" ref={ref} role="dialog" aria-modal={top} aria-label={title} tabIndex={-1} style={{height:initialHeight,scale,y:offset,opacity}}>{contents}</motion.div>}</GlassBackdropContext.Provider></motion.div></div>
}
