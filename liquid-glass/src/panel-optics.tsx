import {useEffect,useId,useLayoutEffect,useRef,useState} from 'react'
import {cachedPanelMap,panelMapKey,requestPanelMap} from './panel-maps'
import {neutralDockMap} from './dock-maps'
import {dockPrism} from './dock-geometry'

type Geometry={width:number;height:number;left:number;top:number;sceneWidth:number;sceneHeight:number;radius:number;background:string}
/** Cards sit over the app ambient. Use that exact opaque source as an isolated
 * filter input instead of a live backdrop that can resample sibling filters.
 * The 12px overscan is painted before displacement; no transparent GPU tiles. */
export function AmbientPanelOptics(){
 const host=useRef<HTMLDivElement>(null),[g,setGeometry]=useState<Geometry|null>(null),id=`panel-optic-${useId().replace(/[^a-zA-Z0-9_-]/g,'')}`
 const [lastMap,setMap]=useState(''),key=g?panelMapKey(g.width,g.height,g.radius):'',image=cachedPanelMap(key)||lastMap||neutralDockMap
 useEffect(()=>{if(!g)return;let current=true;requestPanelMap(g.width,g.height,g.radius).then(url=>{if(current)setMap(url)}).catch(()=>{});return()=>{current=false}},[key])
 useLayoutEffect(()=>{
  const node=host.current,scene=node?.closest<HTMLElement>('.app-shell');if(!node||!scene)return
  const update=()=>{
   const r=node.getBoundingClientRect(),s=scene.getBoundingClientRect(),width=node.clientWidth,height=node.clientHeight;if(!width||!height)return
   const ratio=scene.clientWidth/s.width,ambient=scene.querySelector('.ambient')
   const next={width,height,left:(r.left-s.left)*ratio,top:(r.top-s.top)*ratio,sceneWidth:scene.clientWidth,sceneHeight:scene.clientHeight,radius:parseFloat(getComputedStyle(node).borderTopLeftRadius)||26,background:ambient?getComputedStyle(ambient).backgroundImage:'none'}
   setGeometry(old=>old&&Object.keys(next).every(k=>old[k as keyof Geometry]===next[k as keyof Geometry])?old:next)
  }
  const observer=new ResizeObserver(update);observer.observe(node);observer.observe(scene);document.addEventListener('scroll',update,true)
  return()=>{observer.disconnect();document.removeEventListener('scroll',update,true)}
 },[])
 const p=dockPrism.padding
 return <div className="panel-optics" ref={host} aria-hidden="true">{g&&<>
  <svg className="glass-filter-defs"><defs><filter id={id} filterUnits="userSpaceOnUse" x="0" y="0" width={g.width+p*2} height={g.height+p*2} colorInterpolationFilters="sRGB"><feImage href={image} x="0" y="0" width={g.width+p*2} height={g.height+p*2} preserveAspectRatio="none" result="map"/><feDisplacementMap in="SourceGraphic" in2="map" scale={-dockPrism.bend*255/dockPrism.mapAmplitude} xChannelSelector="R" yChannelSelector="G"/></filter></defs></svg>
  <div className="panel-optic-input" style={{inset:-p,filter:`url(#${id})`}}><div className="panel-optic-environment" style={{width:g.sceneWidth,height:g.sceneHeight,left:p-g.left,top:p-g.top,backgroundImage:g.background}}/></div>
  <span className="panel-optic-veil"/>
 </>}</div>
}
