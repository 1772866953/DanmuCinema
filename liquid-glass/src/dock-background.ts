import defaultSvg from '../public/optical-backdrop.svg?raw'
import {dockPrism} from './dock-geometry'
import RasterWorker from './dock-raster-worker?worker&inline'
type Frame={width:number;height:number;left:number;top:number;sceneWidth:number;sceneHeight:number;dpr:number;backgroundSvg?:string}
const cache=new Map<string,Promise<{rail:string;artwork:string}>>(),paperCache=new Map<string,Promise<string>>()
let rasterWorker:Worker|undefined,sequence=0
const pending=new Map<number,{resolve(url:string):void;reject(error:Error):void}>()
function encode(bitmap:ImageBitmap,width:number,height:number){
 if(!rasterWorker){rasterWorker=new RasterWorker();rasterWorker.onmessage=({data})=>{const job=pending.get(data.id);if(!job)return;pending.delete(data.id);if(data.error)job.reject(new Error(data.error));else job.resolve(data.url)};rasterWorker.onerror=()=>{for(const job of pending.values())job.reject(new Error('Rail raster worker stopped'));pending.clear();rasterWorker?.terminate();rasterWorker=undefined}}
 const id=++sequence
 return new Promise<string>((resolve,reject)=>{pending.set(id,{resolve,reject});rasterWorker!.postMessage({id,bitmap,width,height},[bitmap])})
}
/** Static artwork only: cache the rail's background optics once per layout.
 * Native labels/resting state are composed live afterwards. This is not a DOM
 * screenshot, screen readback, or a per-animation-frame canvas/text rendering. */
export function railBackground(frame:Frame,uv:string){
 const key=JSON.stringify(frame);const saved=cache.get(key);if(saved)return saved
 const result=render(frame,uv);cache.set(key,result);if(cache.size>12)cache.delete(cache.keys().next().value!);result.catch(()=>cache.delete(key));return result
}
function artwork(f:Frame){
 const key=JSON.stringify([f.sceneWidth,f.sceneHeight,f.dpr,f.backgroundSvg]);const saved=paperCache.get(key);if(saved)return saved
 const promise=renderArtwork(f);paperCache.set(key,promise);if(paperCache.size>4)paperCache.delete(paperCache.keys().next().value!);promise.catch(()=>paperCache.delete(key));return promise
}
async function raster(svg:string,width:number,height:number){
 const image=new Image();image.src='data:image/svg+xml,'+encodeURIComponent(svg);await image.decode()
 // Materialize the decoded SVG before recording the canvas draw. Otherwise
 // PNG export synchronously replays a large filtered SVG on the UI thread.
 const bitmap=await createImageBitmap(image,{resizeWidth:width,resizeHeight:height})
 // Flush the draw and encode in a worker, not in a UI-thread canvas export.
 // A transferred ImageBitmap preserves the exact Chromium SVG pixels.
 return encode(bitmap,width,height)
}
async function renderArtwork(f:Frame){
 const parser=new DOMParser()
 let text=defaultSvg,custom=f.backgroundSvg?.trim().replace(/^["']|["']$/g,'')
 if(custom?.startsWith('data:image/svg+xml,'))text=decodeURIComponent(custom.slice(custom.indexOf(',')+1))
 const source=parser.parseFromString(text,'image/svg+xml').documentElement,view=source.getAttribute('viewBox')?.split(/\s+/).map(Number),sw=view?.[2]||Number(source.getAttribute('width'))||1600,sh=view?.[3]||Number(source.getAttribute('height'))||1000
 const cover=Math.max(f.sceneWidth/sw,f.sceneHeight/sh),bw=sw*cover,bh=sh*cover
 source.setAttribute('x',String((f.sceneWidth-bw)/2));source.setAttribute('y',String((f.sceneHeight-bh)/2));source.setAttribute('width',String(bw));source.setAttribute('height',String(bh));source.setAttribute('preserveAspectRatio','none')
 // Same diffuse-artwork policy as AmbientArtwork.kt; no labels enter this cache.
 const pw=Math.max(1,Math.ceil(f.sceneWidth*f.dpr/2)),ph=Math.max(1,Math.ceil(f.sceneHeight*f.dpr/2))
 return raster(`<svg xmlns="http://www.w3.org/2000/svg" width="${pw}" height="${ph}" viewBox="0 0 ${f.sceneWidth} ${f.sceneHeight}"><rect width="100%" height="100%" fill="#a8bbb1"/>${new XMLSerializer().serializeToString(source)}</svg>`,pw,ph)
}
async function render(f:Frame,uv:string){
 const paper=await artwork(f),p=dockPrism.padding,w=f.width+2*p,h=f.height+2*p
 const pw=Math.ceil(w*f.dpr),ph=Math.ceil(h*f.dpr),scale=-dockPrism.bend*255/dockPrism.mapAmplitude
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${pw}" height="${ph}" viewBox="0 0 ${w} ${h}"><defs><filter id="cached-rail-optics" filterUnits="userSpaceOnUse" primitiveUnits="userSpaceOnUse" x="0" y="0" width="${w}" height="${h}" color-interpolation-filters="sRGB"><feImage href="${uv}" x="0" y="0" width="${w}" height="${h}" preserveAspectRatio="none" result="uv"/><feComponentTransfer in="uv" result="map"><feFuncR type="linear" slope="1" intercept="${-.5/255}"/><feFuncG type="linear" slope="1" intercept="${-.5/255}"/></feComponentTransfer><feDisplacementMap in="SourceGraphic" in2="map" scale="${scale}" xChannelSelector="R" yChannelSelector="G"/><feGaussianBlur stdDeviation=".65"/></filter></defs><g filter="url(#cached-rail-optics)"><rect width="${w}" height="${h}" fill="#a8bbb1"/><image href="${paper}" x="${p-f.left}" y="${p-f.top}" width="${f.sceneWidth}" height="${f.sceneHeight}" preserveAspectRatio="none"/></g></svg>`
 return{rail:await raster(svg,pw,ph),artwork:paper}
}
