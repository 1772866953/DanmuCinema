// Rotate the upstream renderer's glyph atlas with the bounded submission queue.
// This avoids writing masks into a texture still read by an earlier GPU frame.
// Foreground glyph masks, ASS layout and color conversion stay unchanged.
export function installAtlasRing(renderer,queue,owner){
 const gl=renderer.gl,slots=Array.from({length:queue.limit},()=>({atlas:null,uploadCache:new Map(),atlasFullAt:0}));let cursor=0,destroyed=false
 slots[0]={atlas:renderer.atlas,uploadCache:renderer.uploadCache,atlasFullAt:renderer.atlasFullAt}
 const render=renderer.render.bind(renderer),destroy=renderer.destroy.bind(renderer)
 renderer.render=(...args)=>{
  const slot=slots[cursor];cursor=(cursor+1)%slots.length
  renderer.atlas=slot.atlas;renderer.uploadCache=slot.uploadCache;renderer.atlasFullAt=slot.atlasFullAt
  // Cached frames do not call createAtlasTexture. Select this slot even when
  // every glyph is already uploaded, rather than sampling the preceding slot.
  if(slot.atlas&&renderer.gl)renderer.gl.bindTexture(renderer.gl.TEXTURE_2D_ARRAY,slot.atlas.texture)
  let result
  try{result=render(...args)}finally{slot.atlas=renderer.atlas;slot.uploadCache=renderer.uploadCache;slot.atlasFullAt=renderer.atlasFullAt}
  queue.submit();return result
 }
 const clear=()=>{for(const slot of slots){if(slot.atlas&&slot.atlas!==renderer.atlas)gl.deleteTexture(slot.atlas.texture);slot.atlas=null;slot.uploadCache.clear();slot.atlasFullAt=0}cursor=0}
 renderer.canvas.addEventListener('webglcontextlost',()=>{clear();queue.reset()},{signal:renderer.ctrl.signal})
 renderer.canvas.addEventListener('webglcontextrestored',()=>{if(owner.cinemaClock?.tick)owner.cinemaClock.tick(performance.now(),'retry')},{signal:renderer.ctrl.signal})
 renderer.destroy=()=>{if(destroyed)return;destroyed=true;owner.cancelGPUWork();queue.reset();clear();destroy()}
}
