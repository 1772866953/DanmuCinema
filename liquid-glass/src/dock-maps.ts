import MapWorker from './dock-map-worker?worker&inline'
type Result={owner:string;sequence:number;key:string;url:string}
type Consumer={applied:number;receive:(url:string,key:string)=>void}
const consumers=new Map<string,Consumer>(),ready=new Map<string,{url:string;image:HTMLImageElement}>()
let worker:Worker|undefined,sequence=0
export const neutralDockMap='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"><rect width="1" height="1" fill="rgb(128,128,128)"/></svg>')
function service(){
 if(worker)return worker
 worker=new MapWorker()
 worker.onmessage=async(event:MessageEvent<Result>)=>{
  const result=event.data,image=new Image();image.src=result.url
  try{await image.decode()}catch{return}
  ready.delete(result.key);ready.set(result.key,{url:result.url,image});if(ready.size>48)ready.delete(ready.keys().next().value!)
  const consumer=consumers.get(result.owner)
  if(consumer&&result.sequence>consumer.applied){consumer.applied=result.sequence;consumer.receive(result.url,result.key)}
 }
 return worker
}
/** One latest pending geometry per lens, not a backlog of stale animation frames.
 * The previous decoded field stays usable until its replacement is decoded. */
export function requestDockMap(owner:string,width:number,height:number,radius:number,receive:Consumer['receive']){
 const key=`${width.toFixed(2)}:${height.toFixed(2)}:${radius.toFixed(2)}`,request=++sequence,consumer=consumers.get(owner)??{applied:0,receive};consumer.receive=receive;consumers.set(owner,consumer)
 const cached=ready.get(key)
 if(cached){consumer.applied=request;receive(cached.url,key);return}
 service().postMessage({owner,sequence:request,key,width,height,radius})
}
export function releaseDockMap(owner:string){consumers.delete(owner);worker?.postMessage({cancel:owner})}
