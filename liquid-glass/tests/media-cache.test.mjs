import test from 'node:test'
import assert from 'node:assert/strict'
import {ThumbnailCache} from '../src/media-cache.ts'
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve}}
const decode=async url=>({decoded:url})

test('navigation reuses successful and absent thumbnails without reloading or decoding',async()=>{
 const cache=new ThumbnailCache();let loads=0,decodes=0
 for(let visit=0;visit<3;visit++){
  await cache.ensure('folder',async()=>{loads++;return 'poster'},async url=>{decodes++;return decode(url)})
  await cache.ensure('empty',async()=>{loads++;return null},decode)
 }
 assert.equal(loads,2);assert.equal(decodes,1);assert.equal(cache.read('folder'),'poster');assert.equal(cache.read('empty'),null)
})
test('concurrent subscribers share a request and receive only decoded images',async()=>{
 const cache=new ThumbnailCache(),download=deferred(),decoding=deferred();let requests=0,changes=0
 cache.subscribe(()=>changes++)
 const load=()=>{requests++;return download.promise},first=cache.ensure('one',load,()=>decoding.promise),second=cache.ensure('one',load,decode)
 download.resolve('image');await Promise.resolve();assert.equal(cache.read('one'),null);assert.equal(changes,0)
 decoding.resolve({});await Promise.all([first,second]);assert.equal(requests,1);assert.equal(cache.read('one'),'image');assert.equal(changes,1)
})
test('refresh retains old pixels until replacement is decoded; old in-flight data cannot win',async()=>{
 const cache=new ThumbnailCache();await cache.ensure('one',async()=>'old',decode)
 cache.invalidate();const stale=deferred(),oldRequest=cache.ensure('one',()=>stale.promise,decode)
 cache.invalidate();const decoding=deferred(),fresh=cache.ensure('one',async()=>'new',()=>decoding.promise)
 await Promise.resolve();assert.equal(cache.read('one'),'old')
 decoding.resolve({});await fresh;stale.resolve('outdated');await oldRequest;assert.equal(cache.read('one'),'new')
})
test('failed refresh preserves a good cover and retries on the next explicit reload',async()=>{
 const cache=new ThumbnailCache();await cache.ensure('one',async()=>'old',decode);cache.invalidate()
 await cache.ensure('one',async()=>{throw new Error('temporary failure')},decode);assert.equal(cache.read('one'),'old')
 cache.invalidate();await cache.ensure('one',async()=>'new',decode);assert.equal(cache.read('one'),'new')
})
test('ordinary state polling never invalidates covers; explicit reload waits for loading to finish',async()=>{
 const cache=new ThumbnailCache();let requests=0;const load=async()=>{requests++;return 'poster'}
 await cache.ensure('one',load,decode)
 cache.observeLoading(true);cache.observeLoading(false);await cache.ensure('one',load,decode);assert.equal(requests,1)
 cache.beginReload();assert.equal(cache.observeLoading(false),false);assert.equal(cache.observeLoading(true),false)
 await cache.ensure('one',load,decode);assert.equal(requests,1)
 assert.equal(cache.observeLoading(false),true);assert.equal(cache.read('one'),'poster');await cache.ensure('one',load,decode);assert.equal(requests,2)
 const id=cache.beginReload();cache.cancelReload(id);cache.observeLoading(true);cache.observeLoading(false);await cache.ensure('one',load,decode);assert.equal(requests,2)
})
