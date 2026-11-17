import {useEffect,useId,useRef,type ReactNode} from 'react'
import {translate,cssMatrix,glassWavelengths,glassSourceMatrix,dockPrism,type Matrix} from './dock-geometry'
import {neutralDockMap,requestDockMap,releaseDockMap} from './dock-maps'
import {railBackground as renderRailBackground} from './dock-background'
const {padding,mapAmplitude}=dockPrism


type Scene={width:number;height:number;left:number;top:number;railWidth:number;railHeight:number;dock:Matrix;railFilter?:string;railImage?:string;artworkImage?:string;backgroundSvg?:string;density?:number}
type LabelPose={left:number;top:number;width:number;height:number;x:number;y:number;magnification:number;strength:number;lift:number;restWidth:number;strain:Matrix}

/** Port of TingYe Android 0.5.0 GlassBackdrop.kt / GlassSegments.kt (MIT).
 * Native and refracted output replay ONE opaque scene: ambient + rail material
 * + resting fill + real DOM labels. No Canvas text or live screen readback.
 * With opaque overscan, RGB screen addition is exact channel selection; glyph
 * alpha is already composited before dispersion, as in the reference shader.
 * Only the rim uses the shader; its zero-shift centre stays native DOM. */
export function useDockOptics(){
 const id=`dock-optics-${useId().replace(/[^a-zA-Z0-9_-]/g,'')}`,image=useRef<SVGFEImageElement>(null),last=useRef('')
 const ambient=useRef<HTMLDivElement>(null),edge=useRef<HTMLDivElement>(null),rim=useRef<SVGSVGElement>(null),rimRect=useRef<SVGRectElement>(null),darkRect=useRef<SVGRectElement>(null),veil=useRef<HTMLSpanElement>(null)
 const records=useRef<(HTMLDivElement|null)[]>([]),environments=useRef<(HTMLDivElement|null)[]>([]),rails=useRef<(HTMLDivElement|null)[]>([]),fills=useRef<(HTMLSpanElement|null)[]>([]),outlines=useRef<(SVGSVGElement|null)[]>([]),sourceRects=useRef<(SVGRectElement|null)[]>([])
 const baseSamples=useRef<(HTMLDivElement|null)[]>([]),baseEnvironments=useRef<(HTMLDivElement|null)[]>([])
 const sourceSamples=useRef<(HTMLDivElement|null)[]>([])
 const environmentImages=useRef<string[]>([])
 const baseImages=useRef<(HTMLImageElement|null)[]>([]),cachedBase=useRef<HTMLDivElement>(null),cachedBaseImage=useRef<HTMLImageElement>(null),baseFallback=useRef<HTMLDivElement>(null)
 const background=useRef(''),artwork=useRef(''),backgroundReady=useRef<(()=>void)|null>(null),backgroundKey=useRef(''),latestBackground=useRef<{width:number;height:number;scene:Scene}|null>(null),uvField=useRef<{url:string;key:string}|null>(null)
 const warps=useRef<(SVGFEDisplacementMapElement|null)[]>([]),maskKey=useRef(''),mask=useRef<SVGMaskElement>(null),maskRect=useRef<SVGRectElement>(null),maskBlur=useRef<SVGFEGaussianBlurElement>(null)
 useEffect(()=>()=>releaseDockMap(id),[id])
 function sizeRim(svg:SVGSVGElement|null,rect:SVGRectElement|null,width:number,height:number){
  attribute(svg,'viewBox',`0 0 ${width} ${height}`)
  if(rect){attribute(rect,'width',width);attribute(rect,'height',height);attribute(rect,'rx',Math.min(width,height)/2)}
 }
 function attribute(node:Element|null,name:string,value:string|number){const text=String(value);if(node&&node.getAttribute(name)!==text)node.setAttribute(name,text)}
 function prepareBackground(){
  const target=latestBackground.current,uv=uvField.current;if(!target||!uv)return
  const {width,height,scene}=target,field=`${width.toFixed(2)}:${height.toFixed(2)}:${(Math.min(width,height)/2).toFixed(2)}`;if(field!==uv.key)return
  const frame={width,height,left:scene.left,top:scene.top,sceneWidth:scene.width,sceneHeight:scene.height,dpr:scene.density??window.devicePixelRatio??1,backgroundSvg:scene.backgroundSvg},key=JSON.stringify(frame)
  if(backgroundKey.current===key)return;backgroundKey.current=key
  renderRailBackground(frame,uv.url).then(async result=>{const image=new Image();image.src=result.rail;await image.decode();if(backgroundKey.current!==key)return;background.current=result.rail;artwork.current=result.artwork;if(cachedBaseImage.current)cachedBaseImage.current.src=result.rail;if(cachedBase.current)cachedBase.current.style.display='block';if(baseFallback.current)baseFallback.current.style.visibility='hidden';backgroundReady.current?.()}).catch(error=>console.error('Rail artwork cache:',error))
 }
 function update(width:number,height:number,_world:Matrix,scene:Scene,includeRail=false,pose?:LabelPose){
  const key=`${width.toFixed(2)}:${height.toFixed(2)}`
  if(!includeRail)latestBackground.current={width,height,scene}
  if(key!==last.current&&image.current){last.current=key;requestDockMap(id,width,height,Math.min(width,height)/2,(url,field)=>{if(image.current){image.current.setAttribute('href',url);image.current.dataset.fieldKey=field;uvField.current={url,key:field};if(!includeRail)prepareBackground()}})}
  if(!includeRail)prepareBackground()
  const strength=pose?.strength??1,base=-dockPrism.bend*255/mapAmplitude*strength
  warps.current.forEach((warp,i)=>attribute(warp,'scale',base*(includeRail?glassWavelengths[i]:1)))
  if(ambient.current)Object.assign(ambient.current.style,{width:`${scene.width}px`,height:`${scene.height}px`,transform:cssMatrix(translate(padding-scene.left,padding-scene.top))})
  if(pose){
   // Keep the GPU filter workspace constant while the visible capsule lifts.
   // Changing its render-target dimensions on every spring step forces Skia
   // to allocate/compile new intermediate surfaces after a cold start/resume.
   // Only the map's placement changes; source pixels and optical scale do not.
   const workspaceWidth=Math.ceil(pose.restWidth*1.4+2*padding),workspaceHeight=Math.ceil((pose.height-8)*1.5+2*padding)
   for(const sample of sourceSamples.current)if(sample){sample.style.width=`${workspaceWidth}px`;sample.style.height=`${workspaceHeight}px`;sample.style.right='auto';sample.style.bottom='auto'}
   attribute(image.current,'width',width+2*padding);attribute(image.current,'height',height+2*padding)
   const transform=cssMatrix(glassSourceMatrix(width,height,pose.left,pose.top,pose.magnification))
   for(let i=0;i<2;i++){
    const record=records.current[i],environment=environments.current[i],rail=rails.current[i],fill=fills.current[i]
    if(record)Object.assign(record.style,{width:`${pose.width}px`,height:`${pose.height}px`,transform})
    if(environment)Object.assign(environment.style,{width:`${scene.width}px`,height:`${scene.height}px`,left:`${-scene.left}px`,top:`${-scene.top}px`})
    if(environment&&scene.artworkImage&&environmentImages.current[i]!==scene.artworkImage){environment.style.backgroundImage=`url("${scene.artworkImage}")`;environmentImages.current[i]=scene.artworkImage}
    if(rail)Object.assign(rail.style,{width:`${pose.width}px`,height:`${pose.height}px`})
    const base=baseSamples.current[i],baseEnvironment=baseEnvironments.current[i]
    if(base)base.style.filter='none'
    const bitmap=baseImages.current[i]
    if(bitmap){if(scene.railImage&&bitmap.getAttribute('src')!==scene.railImage)bitmap.src=scene.railImage;bitmap.style.display=scene.railImage?'block':'none'}
    if(baseEnvironment)baseEnvironment.style.visibility=scene.railImage?'hidden':'visible'
    if(baseEnvironment)Object.assign(baseEnvironment.style,{width:`${scene.width}px`,height:`${scene.height}px`,left:`${padding-scene.left}px`,top:`${padding-scene.top}px`})
    if(fill)Object.assign(fill.style,{left:`${pose.x-pose.restWidth/2}px`,top:`${4+pose.y-pose.height/2}px`,width:`${pose.restWidth}px`,height:`${pose.height-8}px`,opacity:String(.105*(1-pose.lift)),transform:cssMatrix(pose.strain)})
    sizeRim(outlines.current[i],sourceRects.current[i],pose.width,pose.height)
   }
   const dpr=Math.max(1,scene.density??window.devicePixelRatio??1),nextMask=`${width.toFixed(2)}:${height.toFixed(2)}:${dpr}`
   if(nextMask!==maskKey.current){
    maskKey.current=nextMask
    // CSS fragment masks otherwise inherit the zero-sized definitions SVG's
    // viewport: percentage bounds silently erase the entire refracted rim.
    attribute(mask.current,'width',width);attribute(mask.current,'height',height)
    const thickness=Math.min(dockPrism.edgeWidth,width/3,height/3),r=Math.min(width,height)/2
    // Same vector stroke/feather as glassEdgeMask, kept in one SVG resource.
    // Replacing a data-URL mask on every spring frame recreated image decoders
    // and GPU resources, particularly after Chromium discarded idle textures.
    attribute(maskRect.current,'x',thickness/2);attribute(maskRect.current,'y',thickness/2)
    attribute(maskRect.current,'width',width-thickness);attribute(maskRect.current,'height',height-thickness)
    attribute(maskRect.current,'rx',r-thickness/2);attribute(maskRect.current,'stroke-width',thickness)
    attribute(maskBlur.current,'stdDeviation',Math.max(.15,.35/dpr))
   }
  }
  sizeRim(rim.current,rimRect.current,width,height);sizeRim(null,darkRect.current,width,height)
  if(rim.current)rim.current.style.opacity=String(strength)
  veil.current?.style.setProperty('--dock-optics',String(strength))
 }
 const light=(name:string,emphasis:boolean)=><linearGradient id={`${id}-${name}`} x1="0" y1="0" x2="0" y2="1"><stop stopColor="white" stopOpacity={emphasis?.76:.62}/><stop offset="33.333%" stopColor="white" stopOpacity={emphasis?.16:.07}/><stop offset="66.667%" stopColor="var(--dock-ink)" stopOpacity={emphasis?.264:.22}/><stop offset="100%" stopColor="white" stopOpacity={emphasis?.46:.35}/></linearGradient>
 const recording=(i:number,labels:ReactNode)=><div className="dock-recording" ref={node=>{records.current[i]=node}}>
  <div className="dock-optic-environment dock-recorded-ambient" ref={node=>{environments.current[i]=node}}/>
  <div className="dock-source-rail" ref={node=>{rails.current[i]=node}}><div className="dock-source-base-sample" ref={node=>{baseSamples.current[i]=node}}><div className="dock-optic-environment" ref={node=>{baseEnvironments.current[i]=node}}/><img className="dock-background-image" ref={node=>{baseImages.current[i]=node}} alt=""/></div><span className="dock-tint"/><svg className="dock-source-outline" ref={node=>{outlines.current[i]=node}} width="100%" height="100%"><rect ref={node=>{sourceRects.current[i]=node}} fill="none" stroke={`url(#${id}-base-light)`} strokeWidth=".8"/></svg></div>
  <span className="dock-source-selection" ref={node=>{fills.current[i]=node}}/>
  <div className="dock-labels dock-prism-input-labels">{labels}</div>
 </div>
 const material=(includeRail=false,labels?:ReactNode,nativeLabels:ReactNode=labels)=><>
  <svg className="glass-filter-defs" aria-hidden="true"><defs>
   {light('light',includeRail)}{light('base-light',false)}
   <linearGradient id={`${id}-dark`} x1="0" y1="0" x2="0" y2="1"><stop stopColor="var(--dock-edge-ink)" stopOpacity=".099"/><stop offset="50%" stopColor="var(--dock-edge-ink)" stopOpacity=".055"/><stop offset="100%" stopColor="var(--dock-edge-ink)" stopOpacity=".198"/></linearGradient>
   {includeRail&&<><filter id={`${id}-edge-feather`} x="-10%" y="-20%" width="120%" height="140%"><feGaussianBlur ref={maskBlur} stdDeviation=".35"/></filter><mask ref={mask} id={`${id}-edge-mask`} maskUnits="userSpaceOnUse" maskContentUnits="userSpaceOnUse" x="0" y="0" width="1" height="1" style={{maskType:'alpha'}}><rect ref={maskRect} fill="none" stroke="white" filter={`url(#${id}-edge-feather)`}/></mask></>}
   <filter id={id} x="0" y="0" width="100%" height="100%" colorInterpolationFilters="sRGB">
    <feImage ref={image} href={neutralDockMap} width="100%" height="100%" preserveAspectRatio="none" result="map"/>
    <feComponentTransfer in="map" result="neutral-map"><feFuncR type="linear" slope="1" intercept={String(-.5/255)}/><feFuncG type="linear" slope="1" intercept={String(-.5/255)}/></feComponentTransfer>
    {includeRail&&<><feDisplacementMap ref={node=>{warps.current[0]=node}} in="SourceGraphic" in2="neutral-map" scale="0" xChannelSelector="R" yChannelSelector="G" result="red-warp"/><feColorMatrix in="red-warp" type="matrix" values="1 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0" result="red"/></>}
    <feDisplacementMap ref={node=>{warps.current[includeRail?1:0]=node}} in="SourceGraphic" in2="neutral-map" scale="0" xChannelSelector="R" yChannelSelector="G" result="green-warp"/>
    {includeRail&&<><feColorMatrix in="green-warp" type="matrix" values="0 0 0 0 0  0 1 0 0 0  0 0 0 0 0  0 0 0 1 0" result="green"/><feDisplacementMap ref={node=>{warps.current[2]=node}} in="SourceGraphic" in2="neutral-map" scale="0" xChannelSelector="R" yChannelSelector="G" result="blue-warp"/><feColorMatrix in="blue-warp" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 1 0 0  0 0 0 1 0" result="blue"/><feBlend in="red" in2="green" mode="screen" result="rg"/><feBlend in="rg" in2="blue" mode="screen"/></>}
   </filter>
  </defs></svg>
  {includeRail?<>
   <div className="dock-lens-clip"><div className="dock-source-sample dock-native-source" ref={node=>{sourceSamples.current[0]=node}}><div className="dock-source-clip">{recording(0,nativeLabels)}</div></div></div>
   <div ref={edge} className="dock-prism-edge dock-prism-composite" style={{maskImage:`url(#${id}-edge-mask)`}}><div className="dock-source-sample" ref={node=>{sourceSamples.current[1]=node}} style={{filter:`url(#${id})`}}><div className="dock-source-clip">{recording(1,labels)}</div></div></div>
   <span ref={veil} className="dock-lens-veil"/>
  </>:<><div ref={baseFallback} className="dock-optic-sample" style={{filter:`url(#${id}) blur(.65px)`}}><div ref={ambient} className="dock-optic-environment"/></div><div ref={cachedBase} className="dock-optic-sample dock-cached-background"><img ref={cachedBaseImage} className="dock-background-image" alt=""/></div><span className="dock-tint"/></>}
  <svg ref={rim} className={includeRail?'dock-raised-rim':'dock-rail-rim'} width="100%" height="100%" aria-hidden="true">
   {includeRail&&<rect ref={darkRect} fill="none" stroke={`url(#${id}-dark)`} strokeWidth="1.8"/>}
   <rect ref={rimRect} fill="none" stroke={`url(#${id}-light)`} strokeWidth=".8"/>
  </svg>
 </>
 return{material,update,filterId:id,background,artwork,backgroundReady}
}
