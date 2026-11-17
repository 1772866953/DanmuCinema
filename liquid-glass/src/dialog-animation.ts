import {spring} from 'motion'
type Pose={scale:number;y:number;opacity:number}
export function dialogSpringFrames(from:Pose,to:Pose){
 const generators={scale:spring({keyframes:[from.scale,to.scale],stiffness:390,damping:34,mass:.8}),y:spring({keyframes:[from.y,to.y],stiffness:390,damping:34,mass:.8}),opacity:spring({keyframes:[from.opacity,to.opacity],stiffness:390,damping:34,mass:.8})}
 const frames:Keyframe[]=[];let duration=0
 for(let n=0;n<=2400;n++){
  const ms=n*1000/240,s=generators.scale.next(ms),y=generators.y.next(ms),alpha=generators.opacity.next(ms)
  frames.push({transform:`translateY(${y.value}px) scale(${s.value})`,opacity:alpha.value});duration=ms
  if(s.done&&y.done&&alpha.done)break
 }
 return{frames,duration}
}
const standardOpening=dialogSpringFrames({scale:.94,y:16,opacity:0},{scale:1,y:0,opacity:1})
// Native transform/opacity animations keep advancing on the compositor while
// React handles inputs or player state. Sample the existing Motion spring;
// neither its physics nor its final geometry changes.
type DialogAnimationOptions={present:boolean;reduced:boolean;retained:boolean;instantHide:boolean;prepare?:boolean;paintReady?():Promise<unknown>;preparedShade?:Animation}
export function boundedPaintReady(ready:()=>Promise<unknown>,milliseconds=64){
 return new Promise<void>(resolve=>{let done=false;const finish=()=>{if(done)return;done=true;clearTimeout(timer);resolve()},timer=setTimeout(finish,milliseconds);try{Promise.resolve(ready()).then(finish,finish)}catch{finish()}})
}
export function animateNativeDialog(node:HTMLElement,scrim:HTMLElement,options:DialogAnimationOptions):{finished:Promise<void>;cancel():void}{
 // A previously culled form has no painted tiles. Let one actual paint finish
 // before the visible spring starts; input feedback and the saved state remain
 // immediate. An optional GPU acknowledgement is bounded; a compositor which
 // withholds a capture must never leave the menu waiting indefinitely.
 if(options.prepare&&options.present&&Number(getComputedStyle(node).opacity)<.002){
  // Paint the real blurred backdrop before revealing the panel. Keeping the
  // scrim at zero throughout preparation deferred its first raster until the
  // visible panel animation, briefly changing the background beneath it.
  const shade=scrim.animate([{opacity:Number(getComputedStyle(scrim).opacity)},{opacity:1}],{duration:options.reduced?70:200,easing:'ease-out',fill:'both'})
  void shade.finished.catch(()=>{})
  const opacity=node.style.opacity;node.style.opacity='.001';node.dataset.animation='preparing'
  let first=0,second=0,cancelled=false,active:ReturnType<typeof animateNativeDialog>|undefined,complete!:()=>void
  const finished=new Promise<void>((resolve,reject)=>{complete=resolve;first=requestAnimationFrame(()=>{second=requestAnimationFrame(()=>{const begin=()=>{if(cancelled)return;node.style.opacity=opacity;active=animateNativeDialog(node,scrim,{...options,prepare:false,preparedShade:shade});active.finished.then(resolve,reject)};if(options.paintReady)void boundedPaintReady(options.paintReady).then(begin);else begin()})})})
  return{finished,cancel(){cancelled=true;cancelAnimationFrame(first);cancelAnimationFrame(second);if(active)active.cancel();else{scrim.style.opacity=getComputedStyle(scrim).opacity;shade.cancel()}complete()}}
 }
 const {present,reduced,retained,instantHide}=options
 const to={scale:reduced||present?1:.96,y:reduced||present?0:12,opacity:present?1:retained?.001:0},transform=`translateY(${to.y}px) scale(${to.scale})`
 if(!present&&instantHide){Object.assign(node.style,{transform,opacity:String(to.opacity)});scrim.style.opacity='0';return{finished:Promise.resolve(),cancel(){}}}
 // Read both surfaces before starting either animation. Reading a computed
 // style after panel.animate() flushes a partially started animation tree.
 const style=getComputedStyle(node),sourceTransform=style.transform,matrix=new DOMMatrixReadOnly(sourceTransform),from={scale:matrix.a||1,y:matrix.f,opacity:Number(style.opacity)},shadeFrom=Number(getComputedStyle(scrim).opacity),end={transform,opacity:to.opacity}
 const standard=from.scale===.94&&from.y===16&&from.opacity===0
 const animation=present&&!reduced?(standard?standardOpening:dialogSpringFrames(from,to)):{frames:[{transform:sourceTransform,opacity:from.opacity},end],duration:reduced?70:200}
 node.dataset.animation=present?'opening':'closing';node.dataset.animationStart=String(performance.now());node.dataset.animationDuration=String(animation.duration)
 const panel=node.animate(animation.frames,{duration:animation.duration,easing:present&&!reduced?'linear':reduced?'ease-out':'cubic-bezier(.4,0,.2,1)',fill:'both'})
 const shade=options.preparedShade??scrim.animate([{opacity:shadeFrom},{opacity:present?1:0}],{duration:reduced?70:200,easing:'ease-out',fill:'both'})
 const animations=[panel,shade],targets=[node,scrim]
 const finish=()=>{for(let i=0;i<animations.length;i++){if(targets[i].isConnected)animations[i].commitStyles();animations[i].cancel()}}
 const finished=Promise.all(animations.map(animation=>animation.finished)).then(()=>{finish();node.dataset.animation='idle'})
 return{finished,cancel(){for(let i=0;i<animations.length;i++){if(targets[i].isConnected){const current=getComputedStyle(targets[i]);targets[i].style.opacity=current.opacity;if(i===0)targets[i].style.transform=current.transform}animations[i].cancel()}}}
}
