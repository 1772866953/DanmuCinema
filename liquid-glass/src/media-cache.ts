// Renderer-session cache: navigation must not rebuild or re-decode thumbnails.
// The media core already keeps the original JPEGs in its on-disk poster cache.
type Thumbnail={url:string|null;decoded?:unknown;generation:number}
export class ThumbnailCache {
 private entries=new Map<string,Thumbnail>()
 private pending=new Map<string,{generation:number;promise:Promise<void>}>()
 private listeners=new Set<()=>void>()
 private generation=0
 private revision=0
 private reloadId=0
 private reload:{id:number;seenLoading:boolean}|null=null
 subscribe=(listener:()=>void)=>{this.listeners.add(listener);return()=>{this.listeners.delete(listener)}}
 snapshot=()=>this.revision
 epoch=()=>this.generation
 private publish(){this.revision++;for(const listener of this.listeners)listener()}
 read(key:string){return this.entries.get(key)?.url??null}
 async ensure(key:string,load:()=>Promise<unknown>,decode:(url:string)=>Promise<unknown>){
  const generation=this.generation
  if(this.entries.get(key)?.generation===generation)return
  const pending=this.pending.get(key);if(pending?.generation===generation)return pending.promise
  const promise=(async()=>{
   try{
    const value=await load(),url=typeof value==='string'&&value?value:null
    // Keep a decoded image alive and publish only when a new <img> can paint it.
    const decoded=url?await decode(url):undefined
    if(decoded){const images=[...this.entries.values()].filter(v=>v.decoded);if(images.length>=64)images[0].decoded=undefined}
    if(generation!==this.generation)return
    this.entries.set(key,{url,decoded,generation});this.publish()
   }catch{
    // A failed refresh keeps the last good image. Explicit refresh permits retry.
    if(generation===this.generation){const old=this.entries.get(key);this.entries.set(key,{...old,url:old?.url??null,generation})}
   }finally{if(this.pending.get(key)?.generation===generation)this.pending.delete(key)}
  })()
  this.pending.set(key,{generation,promise});return promise
 }
 invalidate(){this.generation++;this.publish()}
 beginReload(loading=false){const id=++this.reloadId;this.reload={id,seenLoading:loading};return id}
 cancelReload(id:number){if(this.reload?.id===id)this.reload=null}
 observeLoading(loading:boolean){
  if(!this.reload)return false
  if(loading){this.reload.seenLoading=true;return false}
  if(!this.reload.seenLoading)return false
  this.reload=null;this.invalidate();return true
 }
}
export const thumbnails=new ThumbnailCache()
export const mediaRowSnapshots=new Map<string,Record<string,any>[]>()
export function rememberMediaRows(key:string,rows:Record<string,any>[]){
 mediaRowSnapshots.delete(key);mediaRowSnapshots.set(key,rows)
 if(mediaRowSnapshots.size>32)mediaRowSnapshots.delete(mediaRowSnapshots.keys().next().value!)
}
export const thumbnailKey=(row:Record<string,any>)=>JSON.stringify([row.id,row.coverKey||row.keys[0],!!row.folder])
export async function decodeThumbnail(url:string){const image=new Image();image.src=url;await image.decode();return image}
