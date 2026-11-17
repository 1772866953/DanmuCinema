import {forwardRef,memo,useContext,useEffect,useImperativeHandle,useLayoutEffect,useRef,useState,type KeyboardEvent,type PointerEvent} from 'react'
import type {LucideIcon} from 'lucide-react'
import {AppearanceContext} from './glass'
import {GlassSegmentMotion,type SegmentFrame} from './segment-motion'
import {useDockOptics} from './dock-optics'
import {translate,multiply,centered,cssMatrix,type Matrix} from './dock-geometry'
import './navigation.css'

export type NavigationItem={id:string;label:string;Icon?:LucideIcon}
export type NavigationHandle={select(id:string):void}
type Props={items:readonly NavigationItem[];value:string;onNavigate(id:string):void;onInteract():void}
const clamp=(x:number,min:number,max:number)=>Math.max(min,Math.min(max,x))

// A cubic rounded rectangle, transformed around its centre. Both native label
// layers use this very silhouette, so no second selection mask can lag behind.
function lensPath(x:number,y:number,w:number,h:number,pose:SegmentFrame){
 const r=Math.min(w,h)/2,k=.5522847498,cx=x+w/2,cy=y+h/2
 const p=(px:number,py:number)=>`${(cx+pose.stretchX*(px-cx)+pose.shear*(py-cy)).toFixed(3)} ${(cy+pose.shear*(px-cx)+pose.stretchY*(py-cy)).toFixed(3)}`
 return `M${p(x+r,y)}L${p(x+w-r,y)}C${p(x+w-r+k*r,y)} ${p(x+w,y+r-k*r)} ${p(x+w,y+r)}L${p(x+w,y+h-r)}C${p(x+w,y+h-r+k*r)} ${p(x+w-r+k*r,y+h)} ${p(x+w-r,y+h)}L${p(x+r,y+h)}C${p(x+r-k*r,y+h)} ${p(x,y+h-r+k*r)} ${p(x,y+h-r)}L${p(x,y+r)}C${p(x,y+r-k*r)} ${p(x+r-k*r,y)} ${p(x+r,y)}Z`
}

export const BottomNavigation=memo(forwardRef<NavigationHandle,Props>(function BottomNavigation({items,value,onNavigate,onInteract},forwarded){
 const {reduced}=useContext(AppearanceContext)
 const root=useRef<HTMLDivElement>(null),body=useRef<HTMLDivElement>(null),labels=useRef<HTMLDivElement>(null),lens=useRef<HTMLDivElement>(null),resting=useRef<HTMLSpanElement>(null)
 const [chosen,setChosen]=useState(value),railOptics=useDockOptics(),lensOptics=useDockOptics()
 const environment=useRef({width:0,height:0,left:0,top:0,backgroundSvg:'',density:1})
 const readyFrame=useRef(0),ready=useRef(false),warming=useRef(false),warmEpoch=useRef(0),resumeEpoch=useRef(0)
 railOptics.backgroundReady.current=()=>{
  paint()
  if(ready.current||warming.current||!railOptics.background.current)return
  // Decode/upload the original-quality optics before exposing a cold window.
  // No lower-resolution placeholder is handed to the first user gesture.
  warming.current=true;const epoch=++warmEpoch.current
  void document.fonts.ready.then(()=>{
   if(!root.current||epoch!==warmEpoch.current)return
   cancelAnimationFrame(frame.current);frame.current=0;clock.current=0
   const m=engine.current;if(!m)return
   let began=0,last=0,released=false,warmedLabel=-1,primed=false
   m.press(center(index(latest.current.value)),geometry.current.slot)
   const warm=(now:number)=>{
    if(!root.current||epoch!==warmEpoch.current)return
    if(engine.current!==m){warming.current=false;railOptics.backgroundReady.current?.();return}
    if(!began)began=now
    const label=Math.min(latest.current.items.length-1,Math.floor((now-began)/55))
    if(label!==warmedLabel){warmedLabel=label;choose(latest.current.items[label].id)}
    const age=now-began
    if(age>=160&&age<900){const q=(age-160)/740,target=center(0)+(center(latest.current.items.length-1)-center(0))*Math.sin(q*Math.PI);m.drag(target,geometry.current.slot,-3,geometry.current.width,0,-3)}
    if(!released&&age>=900){m.release(center(index(latest.current.value)),geometry.current.slot,false);released=true}
    m.step(last?(now-last)/1000:1/60);last=now;paint()
    if(!primed&&age>=500){primed=true;void window.cinema.window('prime').catch(()=>{})}
    if(age<1400){readyFrame.current=requestAnimationFrame(warm);return}
    warming.current=false;choose(latest.current.value);engine.current=new GlassSegmentMotion(center(index(latest.current.value)),geometry.current.slot);paint()
    readyFrame.current=requestAnimationFrame(()=>{if(!root.current)return;ready.current=true;void window.cinema.window('ready').catch(()=>{}).finally(()=>{if(root.current)root.current.dataset.ready='true'})})
   }
   readyFrame.current=requestAnimationFrame(warm)
  })
 }
 const latest=useRef({items,value,onNavigate,reduced});latest.current={items,value,onNavigate,reduced}
 const engine=useRef<GlassSegmentMotion|null>(null),geometry=useRef({width:0,height:60,slot:0}),frame=useRef(0),clock=useRef(0),pending=useRef<string|null>(null),choice=useRef(value)
 const gesture=useRef<{id:number;start:number;startX:number;startY:number;ratio:number;rect:DOMRect;horizontal:boolean;hover:number}|null>(null)
 const index=(id:string)=>Math.max(0,latest.current.items.findIndex(item=>item.id===id))
 const center=(i:number)=>4+geometry.current.slot*(i+.5)
 const nearest=(x:number)=>clamp(Math.round((x-4)/geometry.current.slot-.5),0,latest.current.items.length-1)
 function paint(resuming=false){
  const m=engine.current,host=root.current;if(!m||!host||!body.current||!lens.current||!labels.current)return
  const actual=m.frame(),visible=m.holding||Math.abs(actual.contact)>.001||Math.abs(actual.lift)>.001||Math.abs(actual.stretchX-1)>.0002||Math.abs(actual.stretchY-1)>.0002||Math.abs(actual.shear)>.0002
  // Keep the raised-size GPU surface resident at the existing idle alpha.
  // Shrinking it to the resting capsule made the first press after restore
  // allocate that larger surface again. The visible pose remains unchanged.
  const f=resuming||!visible?{...actual,lift:1,contact:1}:actual,g=geometry.current,h=g.height-8,t=clamp(f.lift,-.08,1.16),lh=h*(1+.40*t)
  // The lens can leave the rail at either end. Its shape is never compressed
  // against the track, and the viewport reserves enough room for full travel.
  const lw=Math.max(1,f.width)*(1+.28*t),left=f.x-lw/2,top=g.height/2+f.y-lh/2
  const matrix=`matrix(${f.stretchX},${f.shear},${f.shear},${f.stretchY},0,0)`
  const env=environment.current,b=.18*f.shear,expansion=.06*f.dock+.18*(f.stretchX-1)
  const half=Math.abs(f.stretchX)*lw/2+Math.abs(f.shear)*lh/2,lo=Math.min(0,f.x-half),hi=Math.max(g.width,f.x+half)
  const sideShear=Math.abs(b)*(Math.max(g.height,lh)/2+Math.abs(f.y))
  // The lens stays full-size beyond the track. Only the heavy rail's horizontal
  // expansion yields when the whole silhouette would touch the window edge.
  const roomLeft=env.left+lo+f.dockX-sideShear-2,roomRight=env.width-env.left-hi-f.dockX-sideShear-2
  const expansionGain=expansion>0?clamp(Math.min(roomLeft/Math.max(.001,expansion*(g.width/2-lo)),roomRight/Math.max(.001,expansion*(hi-g.width/2))),0,1):1
  const dock:Matrix=[1+expansion*expansionGain,b,b,1+.06*f.dock+.18*(f.stretchY-1),f.dockX,f.dockY]
  body.current.style.transform=cssMatrix(dock)
  Object.assign(lens.current.style,{left:`${left}px`,top:`${top}px`,width:`${lw}px`,height:`${lh}px`,transform:matrix})
  lens.current.style.setProperty('--dock-lift',String(clamp(f.lift,0,1)))
  // Keep the local optical pass warm on Chromium. Fully hiding it defers the
  // first GPU filter allocation until DOWN and can stall that first press.
  // At idle this is the same already-composited image over the native scene;
  // original labels remain unmasked and no animation/frame loop stays alive.
  lens.current.style.visibility='visible';lens.current.style.opacity=!resuming&&visible?'1':'.01'
  labels.current.style.clipPath=!resuming&&visible?`path(evenodd,"M-100 -100H${g.width+100}V${g.height+100}H-100Z ${lensPath(left,top,lw,lh,f)}")`:'none'
  if(resting.current)Object.assign(resting.current.style,{left:`${f.x-f.width/2}px`,top:`${4+f.y}px`,width:`${f.width}px`,height:`${h}px`,opacity:String(.105*(1-clamp(actual.lift,0,1))),transform:matrix})
  const bodyMatrix=centered(dock,g.width/2,g.height/2),world=multiply(translate(env.left,env.top),bodyMatrix)
  const scene={...env,railWidth:g.width,railHeight:g.height,dock:bodyMatrix,railFilter:railOptics.filterId,railImage:railOptics.background.current,artworkImage:railOptics.artwork.current}
  railOptics.update(g.width,g.height,world,scene)
  const lensWorld=multiply(multiply(world,translate(left,top)),centered([f.stretchX,f.shear,f.shear,f.stretchY,0,0],lw/2,lh/2))
  lensOptics.update(lw,lh,lensWorld,scene,true,{left,top,width:g.width,height:g.height,x:f.x,y:g.height/2+f.y,magnification:1+.13*clamp(f.lift,0,1),strength:visible?clamp(Math.max(f.lift,f.contact*.65),0,1):.001,lift:clamp(f.lift,0,1),restWidth:f.width,strain:[f.stretchX,f.shear,f.shear,f.stretchY,0,0]})
  if(!resuming){host.dataset.held=String(m.holding);host.dataset.lift=actual.lift.toFixed(4);host.dataset.lensX=actual.x.toFixed(3)}
 }
 function finish(){
  const id=pending.current;if(id===null||gesture.current)return
  pending.current=null
  if(id!==latest.current.value)latest.current.onNavigate(id)
 }
 function tick(now:number){
  frame.current=0
  const more=engine.current?.step(clock.current?(now-clock.current)/1000:1/60);clock.current=now;paint()
  if(engine.current?.landed())finish()
  if(more){frame.current=requestAnimationFrame(tick)}else{clock.current=0;if(root.current)root.current.dataset.motion='settled';finish()}
 }
 function wake(){if(root.current)root.current.dataset.motion='running';if(!frame.current){clock.current=0;frame.current=requestAnimationFrame(tick)}}
 function settleImmediately(id:string){
  cancelAnimationFrame(frame.current);frame.current=0;clock.current=0;engine.current=new GlassSegmentMotion(center(index(id)),geometry.current.slot);paint()
  if(root.current)root.current.dataset.motion='settled';finish()
 }
 function choose(id:string){choice.current=id;setChosen(id)}
 function select(id:string){
  if(!latest.current.items.some(item=>item.id===id)||!engine.current)return
  onInteract()
  if(gesture.current)cancel()
  choose(id);pending.current=null;latest.current.onNavigate(id);engine.current.press(center(index(id)),geometry.current.slot);engine.current.release(center(index(id)),geometry.current.slot)
  if(latest.current.reduced)settleImmediately(id);else wake()
 }
 useImperativeHandle(forwarded,()=>({select}))
 function cancel(){
  const g=gesture.current;gesture.current=null;pending.current=null;choose(latest.current.value)
  if(g&&root.current?.hasPointerCapture(g.id))root.current.releasePointerCapture(g.id)
  engine.current?.release(center(index(latest.current.value)),geometry.current.slot,false)
  if(latest.current.reduced)settleImmediately(latest.current.value);else wake()
 }
 useLayoutEffect(()=>{
  const el=root.current;if(!el)return
  const measure=()=>{
   const width=el.clientWidth,height=el.clientHeight,slot=(width-8)/items.length
   const scene=el.closest<HTMLElement>('.app-shell')!,r=el.getBoundingClientRect(),s=scene.getBoundingClientRect(),ratio=width/r.width
   environment.current={width:scene.clientWidth,height:scene.clientHeight,left:(r.left-s.left)*ratio,top:(r.top-s.top)*ratio,backgroundSvg:getComputedStyle(el).getPropertyValue('--dock-background-svg').trim(),density:(window.devicePixelRatio||1)/ratio}
   if(width===geometry.current.width&&height===geometry.current.height){paint();return}
   if(gesture.current)cancel()
   cancelAnimationFrame(frame.current);frame.current=0;clock.current=0;geometry.current={width,height,slot}
   const id=pending.current??latest.current.value
   engine.current=new GlassSegmentMotion(center(index(id)),slot);paint()
   if(el)el.dataset.motion='settled';finish()
  }
  measure();const observer=new ResizeObserver(measure);observer.observe(el);if(el.closest('.app-shell'))observer.observe(el.closest('.app-shell')!)
  return()=>{observer.disconnect();++warmEpoch.current;cancelAnimationFrame(frame.current);cancelAnimationFrame(readyFrame.current);frame.current=0;clock.current=0}
 },[items.length])
 useEffect(()=>{
  if(gesture.current||pending.current&&pending.current!==value)return
  choose(value);engine.current?.selection(center(index(value)),geometry.current.slot)
  if(reduced)settleImmediately(value);else wake()
 },[value,reduced])
 useEffect(()=>{
  const blur=()=>{if(gesture.current)cancel()}
  const activate=()=>{
   clock.current=0;const epoch=++resumeEpoch.current
   if(document.hidden)return
   // Chromium can discard the raised filter surfaces while hidden. Repaint
   // those exact optics at the existing idle alpha before the first press;
   // input stays available and native labels/resting selection stay untouched.
   if(ready.current&&!gesture.current){paint(true);requestAnimationFrame(()=>{if(epoch!==resumeEpoch.current||gesture.current||document.hidden)return;paint(true);requestAnimationFrame(()=>{if(epoch===resumeEpoch.current&&!gesture.current&&!document.hidden){paint();wake()}})})}
   else{paint();wake()}
  }
  const keyboard=()=>{if(root.current)root.current.dataset.input='keyboard'}
  const visibility=()=>{if(document.hidden){if(gesture.current)cancel();if(pending.current)settleImmediately(pending.current);cancelAnimationFrame(frame.current);frame.current=0;clock.current=0}else activate()}
  window.addEventListener('focus',activate);window.addEventListener('blur',blur);window.addEventListener('keydown',keyboard);document.addEventListener('visibilitychange',visibility)
  return()=>{++resumeEpoch.current;window.removeEventListener('focus',activate);window.removeEventListener('blur',blur);window.removeEventListener('keydown',keyboard);document.removeEventListener('visibilitychange',visibility);cancelAnimationFrame(frame.current)}
 },[])
 function down(e:PointerEvent<HTMLDivElement>){
  if(e.button!==0||gesture.current||!engine.current)return
  if(warming.current){++warmEpoch.current;warming.current=false;cancelAnimationFrame(readyFrame.current);ready.current=true;e.currentTarget.dataset.ready='true';void window.cinema.window('ready').catch(()=>{})}
  onInteract()
  e.currentTarget.dataset.input='pointer'
  const rect=e.currentTarget.getBoundingClientRect(),ratio=geometry.current.width/rect.width,start=nearest((e.clientX-rect.left)*ratio)
  pending.current=null;gesture.current={id:e.pointerId,start,startX:e.clientX,startY:e.clientY,ratio,rect,horizontal:false,hover:start}
  choose(items[start].id);latest.current.onNavigate(items[start].id);engine.current.press(center(start),geometry.current.slot)
  e.currentTarget.setPointerCapture(e.pointerId);e.preventDefault();(e.target as HTMLElement).closest<HTMLButtonElement>('button')?.focus({preventScroll:true})
  if(!reduced)wake()
 }
 function move(e:PointerEvent<HTMLDivElement>){
  const g=gesture.current;if(!g||g.id!==e.pointerId)return
  const dx=(e.clientX-g.startX)*g.ratio,dy=(e.clientY-g.startY)*g.ratio
  if(!g.horizontal&&Math.hypot(dx,dy)>2){if(Math.abs(dy)>Math.abs(dx)){cancel();return}g.horizontal=true}
  if(!g.horizontal)return
  const raw=center(g.start)+dx,bounded=clamp(raw,center(0),center(items.length-1)),pull=14*Math.tanh((raw-bounded)/40),x=bounded+pull
  engine.current?.drag(x,geometry.current.slot,6*Math.tanh(dy/32),geometry.current.width,pull,dy)
  const hovered=nearest(x);if(hovered!==g.hover){g.hover=hovered;choose(items[g.hover].id);latest.current.onNavigate(items[g.hover].id)}if(!reduced)wake();e.preventDefault()
 }
 function up(e:PointerEvent<HTMLDivElement>){
  const g=gesture.current;if(!g||g.id!==e.pointerId)return
  move(e);if(gesture.current!==g)return
  const y=(e.clientY-g.rect.top)*g.ratio,commit=y>=-32&&y<=geometry.current.height+32
  if(!commit){cancel();return}
  gesture.current=null
  const id=items[g.hover].id;choose(id);pending.current=null;latest.current.onNavigate(id)
  root.current?.querySelectorAll<HTMLButtonElement>('[role=tab]')[g.hover]?.focus({preventScroll:true})
  engine.current?.release(center(g.hover),geometry.current.slot)
  if(e.currentTarget.hasPointerCapture(e.pointerId))e.currentTarget.releasePointerCapture(e.pointerId)
  if(reduced)settleImmediately(id);else wake()
 }
 function keyboard(e:KeyboardEvent<HTMLButtonElement>,i:number){
  let target=i;const rtl=getComputedStyle(root.current!).direction==='rtl'
  if(e.key==='ArrowRight')target=(i+(rtl?-1:1)+items.length)%items.length
  else if(e.key==='ArrowLeft')target=(i+(rtl?1:-1)+items.length)%items.length
  else if(e.key==='Home')target=0;else if(e.key==='End')target=items.length-1;else return
  e.preventDefault();root.current?.querySelectorAll<HTMLButtonElement>('[role=tab]')[target]?.focus();select(items[target].id)
 }
 const labelRow=(layer:string)=><>{items.map(({id,label,Icon})=><span key={id} className="dock-label" data-active={chosen===id} data-layer={layer}>{Icon&&<Icon size={18}/>}<span>{label}</span></span>)}</>
 return <nav className="bottom-navigation" aria-label="主要导航">
  <div ref={root} className="dock-hit-surface" role="tablist" aria-label="工作空间" data-selected={chosen} style={{'--dock-count':items.length} as React.CSSProperties} onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={()=>{if(gesture.current)cancel()}} onLostPointerCapture={()=>{if(gesture.current)cancel()}}>
   <div ref={body} className="dock-body" aria-hidden="true">
    <div className="dock-material">{railOptics.material()}</div>
    <span ref={resting} className="dock-resting-selection"/>
    <div ref={labels} className="dock-labels">{labelRow('base')}</div>
    <div ref={lens} className="dock-lens"><div className="dock-lens-glass">{lensOptics.material(true,labelRow('edge'),labelRow('lens'))}</div></div>
   </div>
   <div className="dock-targets">{items.map(({id,label},i)=><button key={id} id={`workspace-tab-${id}`} role="tab" aria-selected={value===id} aria-controls="workspace-page" tabIndex={chosen===id?0:-1} title={label} onClick={e=>{if(e.detail===0)select(id)}} onKeyDown={e=>keyboard(e,i)}><span className="sr-only">{label}</span></button>)}</div>
  </div>
 </nav>
}))
