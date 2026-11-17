const {test}=require('node:test'),assert=require('node:assert/strict')
async function setup(){
 const {installAtlasRing}=await import('../player-overlay/ass-gpu-adapter.js')
 let next=0,bound=null,cancelled=0,resets=0,submitted=0,repainted=0
 const deleted=[],samples=[],canvas=new EventTarget(),ctrl=new AbortController()
 const gl={TEXTURE_2D_ARRAY:1,bindTexture(_,texture){bound=texture},deleteTexture(texture){deleted.push(texture)}}
 const renderer={gl,canvas,ctrl,atlas:{texture:++next},uploadCache:new Map([['initial',1]]),atlasFullAt:12,
  render(value){if(!this.atlas){this.atlas={texture:++next};gl.bindTexture(1,this.atlas.texture)}this.uploadCache.set(value,this.atlas.texture);samples.push({texture:this.atlas.texture,bound,initial:this.uploadCache.has('initial')});if(value==='error')throw Error('upload failed')},
  destroy(){this.ctrl.abort();if(this.atlas)gl.deleteTexture(this.atlas.texture);this.atlas=this.gl=null}}
 const queue={limit:3,submit(){submitted++},reset(){resets++}},owner={cancelGPUWork(){cancelled++},cinemaClock:{tick(){repainted++}}}
 installAtlasRing(renderer,queue,owner)
 return{renderer,canvas,deleted,samples,stats:()=>({cancelled,resets,submitted,repainted}),restore(){renderer.gl=gl;renderer.atlas=null;renderer.uploadCache.clear();canvas.dispatchEvent(new Event('webglcontextrestored'))}}
}
test('atlas rotation rebinds cached masks and preserves the initial texture',async()=>{
 const s=await setup();for(let n=0;n<9;n++)s.renderer.render(n)
 assert.deepEqual(s.samples.map(x=>x.texture),[1,2,3,1,2,3,1,2,3]);assert(s.samples.every(x=>x.texture===x.bound));assert(s.samples[0].initial);assert(s.samples[3].initial)
 assert.equal(s.stats().submitted,9);s.renderer.destroy();s.renderer.destroy();assert.deepEqual(s.deleted.sort(),[1,2,3]);assert.equal(s.stats().cancelled,1)
})
test('context loss clears all ring caches and redraws after restoration',async()=>{
 const s=await setup();for(let n=0;n<3;n++)s.renderer.render(n)
 s.renderer.gl=null;s.canvas.dispatchEvent(new Event('webglcontextlost'));assert.equal(s.stats().resets,1)
 s.restore();assert.equal(s.stats().repainted,1);for(let n=0;n<3;n++)s.renderer.render(n)
 // The active texture (3) is reclaimed with the lost context; the adapter
 // explicitly retires its other slots and all textures created after restore.
 assert(s.samples.slice(3).every(x=>!x.initial));s.renderer.destroy();assert.deepEqual(s.deleted.sort(),[1,2,4,5,6])
})
test('an interrupted upload retains its resources for cleanup without submitting a frame',async()=>{
 const s=await setup();s.renderer.render('first');assert.throws(()=>s.renderer.render('error'),/upload failed/);assert.equal(s.stats().submitted,1)
 s.renderer.destroy();assert.deepEqual(s.deleted.sort(),[1,2])
})
