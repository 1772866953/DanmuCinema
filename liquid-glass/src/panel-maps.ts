import MapWorker from './panel-map-worker?worker&inline'
const ready=new Map<string,{url:string;image:HTMLImageElement}>(),pending=new Map<string,{promise:Promise<string>;resolve(value:string):void;reject(error:Error):void}>()
let worker:Worker|undefined
export const panelMapKey=(width:number,height:number,radius:number)=>`${Math.round(width*2)}:${Math.round(height*2)}:${radius.toFixed(2)}`
export const cachedPanelMap=(key:string)=>ready.get(key)?.url||''
export const waitPanelMaps=()=>Promise.allSettled([...pending.values()].map(job=>job.promise))
export function requestPanelMap(width:number,height:number,radius:number){
 const key=panelMapKey(width,height,radius),saved=ready.get(key);if(saved)return Promise.resolve(saved.url)
 const current=pending.get(key);if(current)return current.promise
 if(!worker){worker=new MapWorker();worker.onmessage=async({data})=>{const job=pending.get(data.key);if(!job)return;try{if(!data.url)throw Error(data.error);const image=new Image();image.src=data.url;await image.decode();if(ready.size>=32)ready.delete(ready.keys().next().value!);ready.set(data.key,{url:data.url,image});pending.delete(data.key);job.resolve(data.url)}catch(error){pending.delete(data.key);job.reject(error instanceof Error?error:new Error(String(error)))}};worker.onerror=()=>{for(const job of pending.values())job.reject(Error('Panel map worker stopped'));pending.clear();worker?.terminate();worker=undefined}}
 let resolve!:(value:string)=>void,reject!:(error:Error)=>void;const promise=new Promise<string>((yes,no)=>{resolve=yes;reject=no});pending.set(key,{promise,resolve,reject});worker.postMessage({key,width,height,radius});return promise
}
