const {test}=require('node:test'),assert=require('node:assert/strict')
const {createTrayWindowCache}=require('../electron/tray-window-cache.cjs')
function fixture(duration=20000){
 const callbacks=[],released=[],windows=[];let current
 const cache=createTrayWindowCache({duration,getCurrent:()=>current,release:w=>{w.destroyed=true;released.push(w)},schedule:(fn,ms)=>{const handle={fn,ms,cancelled:false,unref(){}};callbacks.push(handle);return handle},cancel:h=>{h.cancelled=true}})
 function window(){const w={destroyed:false,visible:true,isDestroyed(){return this.destroyed},isVisible(){return this.visible},hide(){this.visible=false}};current=w;windows.push(w);return w}
 return{cache,callbacks,released,window}
}
test('quick restoration reuses the renderer and a cancelled expiry cannot destroy it',()=>{
 const f=fixture(),w=f.window();assert.equal(f.cache.park(w),true);assert.equal(w.visible,false);assert.equal(f.callbacks[0].ms,20000)
 assert.equal(f.cache.resume(w),true);w.visible=true;f.callbacks[0].fn();assert.deepEqual(f.released,[]);assert.equal(f.cache.isParked(w),false)
})
test('a long tray stay releases the hidden renderer after the grace interval',()=>{
 const f=fixture(),w=f.window();f.cache.park(w);f.callbacks[0].fn();assert.deepEqual(f.released,[w]);assert.equal(f.cache.isParked(w),false)
})
test('repeated close resets the grace interval and a stale callback cannot expire the newer lease',()=>{
 const f=fixture(),w=f.window();f.cache.park(w);const old=f.callbacks[0];f.cache.resume(w);w.visible=true;f.cache.park(w);old.fn();assert.deepEqual(f.released,[]);assert.equal(f.cache.isParked(w),true);f.callbacks[1].fn();assert.deepEqual(f.released,[w])
})
test('replaced, already closed and externally shown windows are never destroyed by expiry',()=>{
 for(const mode of ['replaced','closed','visible']){const f=fixture(),w=f.window();f.cache.park(w);if(mode==='replaced')f.window();if(mode==='closed')w.destroyed=true;if(mode==='visible')w.visible=true;f.callbacks[0].fn();assert.deepEqual(f.released,[])}
})
test('shutdown and normal window closure cancel pending retention',()=>{
 for(const action of ['dispose','forget']){const f=fixture(),w=f.window();f.cache.park(w);f.cache[action](w);assert.equal(f.callbacks[0].cancelled,true);f.callbacks[0].fn();assert.deepEqual(f.released,[])}
 const f=fixture(0),w=f.window();assert.equal(f.cache.park(w),false);assert.equal(w.visible,true);assert.equal(f.callbacks.length,0)
})
