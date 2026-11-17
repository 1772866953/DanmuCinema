import {memo,useCallback,useLayoutEffect,useRef,type KeyboardEvent,type PointerEvent} from 'react'
import './duration-wheel.css'

const maximum=30*24*60*60
const bounded=(value:number,max:number)=>Math.max(0,Math.min(max,Math.round(value)||0))

/** Native scroll snapping keeps the digits sharp: no scaled text or filtered layer. */
const WheelColumn=memo(function WheelColumn({label,unit,value,max,disabled,onChange}:{label:string;unit:string;value:number;max:number;disabled:boolean;onChange(value:number):void}){
 const viewport=useRef<HTMLDivElement>(null),current=useRef(value),initial=useRef(true),syncing=useRef(false)
 const drag=useRef<{id:number;y:number;top:number;lastY:number;lastTime:number;velocity:number;moved:boolean}|null>(null)
 const suppressClick=useRef(false),typed=useRef({digits:'',at:0})
 const requested=useRef<number|null>(null)
 const behavior=():ScrollBehavior=>matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'
 const rowHeight=()=>viewport.current!.querySelector<HTMLElement>('.duration-option')!.offsetHeight
 const select=(next:number)=>{const node=viewport.current;if(node&&!disabled){syncing.current=false;requested.current=bounded(next,max);node.scrollTo({top:requested.current*rowHeight(),behavior:behavior()})}}
 useLayoutEffect(()=>{
  const node=viewport.current;if(!node)return
  if(initial.current||value!==current.current){current.current=value;syncing.current=true;requested.current=value;node.scrollTo({top:value*rowHeight(),behavior:initial.current||disabled?'instant':behavior()});initial.current=false}
 },[value,max])
 useLayoutEffect(()=>{
  const node=viewport.current;if(!node)return
  // Re-align after UI zoom or responsive row-height changes, without resetting a gesture.
  let previousRow=rowHeight()
  const observer=new ResizeObserver(()=>{const height=rowHeight();if(height===previousRow)return;previousRow=height;if(!drag.current)node.scrollTo({top:(requested.current??current.current)*height,behavior:'instant'})})
  const settled=()=>{syncing.current=false;requested.current=null};node.addEventListener('scrollend',settled)
  observer.observe(node);return()=>{observer.disconnect();node.removeEventListener('scrollend',settled)}
 },[])
 const scroll=()=>{
  const node=viewport.current;if(!node||disabled||syncing.current)return
  const next=bounded(node.scrollTop/rowHeight(),max)
  if(next===current.current)return
  current.current=next;onChange(next)
 }
 const down=(event:PointerEvent<HTMLDivElement>)=>{
  if(disabled||event.pointerType!=='mouse'||event.button!==0)return
  const node=viewport.current!;node.focus({preventScroll:true});node.scrollTo({top:node.scrollTop,behavior:'instant'})
  syncing.current=false;requested.current=null;suppressClick.current=false;drag.current={id:event.pointerId,y:event.clientY,top:node.scrollTop,lastY:event.clientY,lastTime:event.timeStamp,velocity:0,moved:false}
 }
 const move=(event:PointerEvent<HTMLDivElement>)=>{
  const d=drag.current,node=viewport.current;if(!d||!node||d.id!==event.pointerId)return
  const distance=event.clientY-d.y
  if(!d.moved&&Math.abs(distance)<4)return
  if(!d.moved){d.moved=true;node.dataset.dragging='true';node.setPointerCapture(event.pointerId)}
  event.preventDefault()
  const elapsed=event.timeStamp-d.lastTime
  if(elapsed>0)d.velocity=(d.lastY-event.clientY)/elapsed
  d.lastTime=event.timeStamp;d.lastY=event.clientY
  node.scrollTop=d.top-distance
 }
 const finish=(event:PointerEvent<HTMLDivElement>,cancelled=false)=>{
  const d=drag.current,node=viewport.current;if(!d||!node||d.id!==event.pointerId)return
  drag.current=null;delete node.dataset.dragging
  if(node.hasPointerCapture(event.pointerId))node.releasePointerCapture(event.pointerId)
  if(!d.moved)return
  suppressClick.current=true
  const velocity=!cancelled&&event.timeStamp-d.lastTime<100?Math.max(-2.5,Math.min(2.5,d.velocity)):0
  select(Math.round((node.scrollTop+velocity*120)/rowHeight()))
 }
 const key=(event:KeyboardEvent<HTMLDivElement>)=>{
  if(disabled)return
  const steps:Record<string,number>={ArrowUp:-1,ArrowDown:1,PageUp:-10,PageDown:10}
  if(event.key in steps){event.preventDefault();select((requested.current??current.current)+steps[event.key]);return}
  if(event.key==='Home'||event.key==='End'){event.preventDefault();select(event.key==='Home'?0:max);return}
  if(/^\d$/.test(event.key)){event.preventDefault();const now=performance.now(),last=typed.current;const digits=(now-last.at<800?last.digits:'')+event.key;typed.current={digits,at:now};select(Number(digits))}
 }
 return <div className="duration-column">
  <div ref={viewport} className="duration-wheel" role="spinbutton" tabIndex={disabled?-1:0} aria-label={label} aria-valuemin={0} aria-valuemax={max} aria-valuenow={value} aria-valuetext={`${value} ${unit}`} aria-disabled={disabled} onScroll={scroll} onWheel={()=>{syncing.current=false;requested.current=null}} onKeyDown={key} onPointerDown={down} onPointerMove={move} onPointerUp={event=>finish(event)} onPointerCancel={event=>finish(event,true)} onLostPointerCapture={event=>finish(event,true)} onClickCapture={event=>{if(suppressClick.current){suppressClick.current=false;event.preventDefault();event.stopPropagation()}}}>
   {Array.from({length:max+1},(_,n)=><div key={n} className="duration-option" data-selected={n===value||undefined} aria-hidden="true" onClick={()=>select(n)}>{String(n).padStart(2,'0')}</div>)}
  </div>
  <span className="duration-unit" aria-hidden="true">{unit}</span>
 </div>
})

export function DurationWheel({seconds,onChange,disabled=false}:{seconds:number;onChange(seconds:number):void;disabled?:boolean}){
 const value=bounded(seconds,maximum),latest=useRef(value);latest.current=value
 const setHours=useCallback((hours:number)=>onChange(Math.min(maximum,hours*3600+latest.current%3600)),[onChange])
 const setMinutes=useCallback((minutes:number)=>onChange(Math.floor(latest.current/3600)*3600+minutes*60+latest.current%60),[onChange])
 const setSeconds=useCallback((part:number)=>onChange(Math.floor(latest.current/60)*60+part),[onChange])
 const atLimit=value===maximum
 return <div className="duration-picker" role="group" aria-label="倒计时时长" data-disabled={disabled||undefined}>
  <div className="duration-selection" aria-hidden="true"/>
  <WheelColumn label="倒计时小时" unit="小时" value={Math.floor(value/3600)} max={720} disabled={disabled} onChange={setHours}/>
  <WheelColumn label="倒计时分钟" unit="分钟" value={Math.floor(value/60)%60} max={atLimit?0:59} disabled={disabled} onChange={setMinutes}/>
  <WheelColumn label="倒计时秒" unit="秒" value={value%60} max={atLimit?0:59} disabled={disabled} onChange={setSeconds}/>
 </div>
}

export function ClockWheel({value,onChange,disabled=false}:{value:string;onChange(value:string):void;disabled?:boolean}){
 const parts=value.split(':').map(Number),hours=bounded(parts[0],23),minutes=bounded(parts[1],59),seconds=bounded(parts[2],59)
 const update=(index:number,next:number)=>{const time=[hours,minutes,seconds];time[index]=next;onChange(time.map(n=>String(n).padStart(2,'0')).join(':'))}
 return <div className="duration-picker time-of-day" role="group" aria-label="执行时间" data-disabled={disabled||undefined}>
  <div className="duration-selection" aria-hidden="true"/>
  <WheelColumn label="执行时间小时" unit="时" value={hours} max={23} disabled={disabled} onChange={next=>update(0,next)}/>
  <WheelColumn label="执行时间分钟" unit="分" value={minutes} max={59} disabled={disabled} onChange={next=>update(1,next)}/>
  <WheelColumn label="执行时间秒" unit="秒" value={seconds} max={59} disabled={disabled} onChange={next=>update(2,next)}/>
 </div>
}
