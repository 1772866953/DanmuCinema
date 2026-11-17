// Share one in-flight renderer; duplicate canvases would retain older text.
export function getUILayer(layers,pending,id,create){
 if(layers.has(id))return Promise.resolve(layers.get(id))
 if(pending.has(id))return pending.get(id)
 const request=Promise.resolve().then(()=>create(id)).then(layer=>{layers.set(id,layer);return layer})
 pending.set(id,request);request.then(()=>pending.delete(id),()=>pending.delete(id))
 return request
}
