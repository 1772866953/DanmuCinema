const {test}=require('node:test'),assert=require('node:assert/strict')
test('concurrent first UI frames share a single canvas renderer and a failed creation can retry',async()=>{
 const {getUILayer}=await import('../player-overlay/ui-layer-cache.js'),layers=new Map(),pending=new Map();let creates=0,finish
 const create=()=>{creates++;return new Promise(resolve=>finish=resolve)}
 const a=getUILayer(layers,pending,'menu',create),b=getUILayer(layers,pending,'menu',create)
 assert.equal(a,b);await Promise.resolve();assert.equal(creates,1)
 const layer={canvas:'one'};finish(layer);assert.equal(await a,layer);assert.equal(await getUILayer(layers,pending,'menu',create),layer);assert.equal(creates,1)
 await assert.rejects(getUILayer(layers,pending,'new',()=>Promise.reject(Error('lost'))),/lost/)
 assert(!pending.has('new'));assert.equal(await getUILayer(layers,pending,'new',()=>layer),layer)
})
