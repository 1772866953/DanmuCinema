import {useLayoutEffect,useRef,type ReactNode} from 'react'
import {createPortal} from 'react-dom'
import {motion} from 'motion/react'
import {fitFloatingMenu} from './floating-placement'

export function MediaContextMenu({x,y,onClose,children}:{x:number;y:number;onClose():void;children:ReactNode}){
 const host=useRef<HTMLDivElement>(null)
 useLayoutEffect(()=>{
  const node=host.current;if(!node)return
  const place=()=>{const borders=node.offsetHeight-node.clientHeight,p=fitFloatingMenu(x,y,node.offsetWidth,node.scrollHeight+borders,innerWidth,innerHeight);Object.assign(node.style,{left:`${p.left}px`,top:`${p.top}px`,maxWidth:`${p.maxWidth}px`,maxHeight:`${p.maxHeight}px`})}
  place();const observer=new ResizeObserver(place);observer.observe(node);window.addEventListener('resize',place)
  const key=(event:KeyboardEvent)=>{if(event.key==='Escape'){event.preventDefault();event.stopPropagation();onClose()}}
  window.addEventListener('keydown',key)
  return()=>{observer.disconnect();window.removeEventListener('resize',place);window.removeEventListener('keydown',key)}
 },[x,y,onClose])
 return createPortal(<motion.div ref={host} className="context-menu media-context-menu" role="menu" aria-label="影片操作" style={{left:x,top:y}} initial={{opacity:0,y:-4}} animate={{opacity:1,y:0}} exit={{opacity:0}} onPointerDown={event=>event.stopPropagation()}>{children}</motion.div>,document.body)
}
