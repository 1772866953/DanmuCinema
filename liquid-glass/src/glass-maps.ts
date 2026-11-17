import MapWorker from './glass-map-worker?worker&inline'
const ready=new Map<string,{url:string;image:HTMLImageElement}>(),pending=new Map<string,{resolve:(url:string)=>void;reject:(error:Error)=>void;promise:Promise<string>}>()
let worker:Worker|undefined
const publications:(()=>void)[]=[]
let publishing=false
function publishNext(){
 const publish=publications.shift();publish?.()
 if(publications.length)requestAnimationFrame(publishNext);else publishing=false
}
function publish(callback:()=>void){publications.push(callback);if(!publishing){publishing=true;requestAnimationFrame(publishNext)}}
export const glassMapKey=(width:number,height:number,radius:number)=>`${width}:${height}:${radius}`
export const cachedGlassMap=(key:string)=>ready.get(key)?.url||''
export const waitGlassMaps=()=>Promise.allSettled([...pending.values()].map(job=>job.promise))
export function requestGlassMap(width:number,height:number,radius:number){
 const key=glassMapKey(width,height,radius),cached=ready.get(key);if(cached)return Promise.resolve(cached.url)
 const existing=pending.get(key);if(existing)return existing.promise
 if(!worker){
  worker=new MapWorker()
  worker.onmessage=async({data}:{data:{key:string;url?:string;error?:string}})=>{
   const request=pending.get(data.key);if(!request)return
   try{
    if(!data.url)throw new Error(data.error||'Glass map unavailable')
    const image=new Image();image.src=data.url;await image.decode()
    const url=data.url
    // Avoid introducing every new GPU backdrop/filter surface in one commit
    // when a page with many controls is visited for the first time.
    publish(()=>{if(pending.get(data.key)!==request)return;if(ready.size>=160)ready.delete(ready.keys().next().value!);ready.set(data.key,{url,image});pending.delete(data.key);request.resolve(url)})
   }catch(error){pending.delete(data.key);request.reject(error instanceof Error?error:new Error(String(error)))}
  }
  worker.onerror=()=>{for(const request of pending.values())request.reject(new Error('Glass map worker stopped'));pending.clear();worker?.terminate();worker=undefined}
 }
 let resolve!:(url:string)=>void,reject!:(error:Error)=>void
 const promise=new Promise<string>((yes,no)=>{resolve=yes;reject=no});pending.set(key,{promise,resolve,reject});worker.postMessage({key,width,height,radius});return promise
}
