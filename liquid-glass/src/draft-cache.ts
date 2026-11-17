// A renderer-local cache avoids synchronous storage IPC on every page mount.
// Actual edits are still persisted before returning; tray destruction therefore
// cannot discard a pending idle/debounce write.
export function createDraftCache(storage:()=>Pick<Storage,'getItem'|'setItem'>){
 const values=new Map<string,string|null>()
 const read=<T>(key:string,fallback:T):T=>{try{if(!values.has(key))values.set(key,storage().getItem(key));const text=values.get(key);return text==null?fallback:JSON.parse(text)}catch{return fallback}}
 const write=(key:string,value:unknown)=>{try{const text=JSON.stringify(value);if(values.has(key)&&values.get(key)===text)return;storage().setItem(key,text);values.set(key,text)}catch{}}
 const invalidate=(key:string|null)=>{if(key===null)values.clear();else values.delete(key)}
 return{read,write,invalidate}
}
export const draftCache=createDraftCache(()=>localStorage)
if(typeof window!=='undefined')window.addEventListener('storage',event=>{if(event.storageArea===localStorage)draftCache.invalidate(event.key)})
