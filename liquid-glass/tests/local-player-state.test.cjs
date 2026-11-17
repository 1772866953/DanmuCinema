const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),{LocalPlayer,fileKey}=require('../electron/player.cjs')
async function fixture(t){const storage=await fs.mkdtemp(path.join(os.tmpdir(),'DanmuCinema-unit-player-')),player=new LocalPlayer({storage});await player.ready;t.after(async()=>{clearTimeout(player.publishTimer);await player.saveChain;assert(path.dirname(storage)===os.tmpdir()&&path.basename(storage).startsWith('DanmuCinema-unit-player-'));await fs.rm(storage,{recursive:true,force:true})});return player}
test('playback waits for all native UI resources and cancels waiting on session change',async t=>{
 const p=await fixture(t);let polls=0;p.ipc={command:async()=>{polls++;return polls<3?{'cinema-ready-danmu':true}:Object.fromEntries(['danmu','input','modernz','panel'].map(role=>['cinema-ready-'+role,true]))}}
 assert.equal(await p.prepareUi(),true);assert.equal(polls,3)
 p.ipc={command:async()=>{p.generation++;return {}}};assert.equal(await p.prepareUi(),false)
})
test('mpv clears duration metadata on exit without erasing the saved resume point',async t=>{
 const p=await fixture(t);p.current={path:__filename,size:1234,mtime:10};p.state.position=11;p.state.duration=30
 await p.event({event:'property-change',name:'duration',data:null});p.checkpoint();await p.persist()
 const entry=JSON.parse(await fs.readFile(path.join(p.storage,'history.json'),'utf8'))[fileKey(__filename)];assert.equal(entry.position,11);assert.equal(entry.duration,30)
 p.state.duration=0;p.checkpoint();assert.equal(p.history[fileKey(__filename)].duration,30)
})
test('a rejected in-flight operation during normal shutdown does not publish a playback fault',async t=>{
 const p=await fixture(t);let faults=0;p.on('fault',()=>faults++);p.closing=true;p.fail(new Error('IPC closed during shutdown'));assert.equal(faults,0);assert.equal(p.state.error,undefined)
 p.closing=false;p.fail(new Error('unexpected playback fault'));assert.equal(faults,1);assert.equal(p.state.error,'unexpected playback fault')
})

test('turning off online matching cancels its pending token without clearing local danmu',async t=>{
 const p=await fixture(t);let cancelled;p.cancelPrepare=token=>cancelled=token;p.prepareToken='pending';p.state.danmuCount=123;p.state.danmu='已加载本地弹幕';p.setAutomaticMatching(false)
 assert.equal(cancelled,'pending');assert.equal(p.prepareToken,null);assert.equal(p.state.danmuCount,123);assert.equal(p.state.danmu,'已加载本地弹幕');p.setAutomaticMatching(true);assert.equal(p.automaticMatching,true)
})

test('native option bursts coalesce to the latest value without blocking input',async t=>{
 const p=await fixture(t),calls=[];p.configure=async value=>calls.push(value);p.queueNativeOptions({fontSize:30});p.queueNativeOptions({fontSize:35,opacity:.7});p.queueNativeOptions({fontSize:40});assert.equal(calls.length,0);await new Promise(r=>setTimeout(r,90));assert.deepEqual(calls,[{fontSize:40,opacity:.7}]);assert.equal(p.nativePatch,null)
})
test('closing keeps pending preferences without waiting for layout work',async t=>{
 const p=await fixture(t);p.nativePatch={fontSize:45};p.configure=async()=>{throw Error('close must not start layout work')};await p.stop();assert.equal(p.settings.fontSize,45);assert.equal(p.nativePatch,null);assert.equal(p.nativeTimer,null)
})

test('a newer desktop preference wins over an older delayed native click',async t=>{
 const p=await fixture(t);p.queueNativeOptions({fontSize:35});await p.configure({fontSize:40});await new Promise(r=>setTimeout(r,90));assert.equal(p.settings.fontSize,40);assert.equal(p.nativePatch,null);assert.equal(p.nativeTimer,null)
})

for(const blocked of ['delayed','failed'])test(`live FPS reaches mpv before ${blocked} settings persistence`,async t=>{
 const p=await fixture(t),calls=[];p.current={path:__filename,size:1234,mtime:10};p.files=[p.current];p.ipc={command:async args=>calls.push(args)};let entered=false,release;p.persist=()=>{entered=true;return blocked==='failed'?Promise.reject(new Error('EPERM')):new Promise(r=>release=r)}
 const pending=p.configure({danmuFps:120});await new Promise(r=>setImmediate(r));assert(entered);const applied=calls.find(a=>a[0]==='script-message-to'&&a[2]==='cinema-options');assert.equal(JSON.parse(applied[3]).danmuFps,120);assert.equal(p.snapshot().settings.danmuFps,120);if(release)release();await pending;if(blocked==='failed')assert.match(p.state.notice,/设置已生效/)
})
test('FPS preference persists across player instances',async t=>{
 const p=await fixture(t);await p.configure({danmuFps:60});const restored=new LocalPlayer({storage:p.storage});await restored.ready;assert.equal(restored.settings.danmuFps,60)
})

test('preload changes apply to a playing file, persist, and do not reinitialize video or danmu',async t=>{
 const p=await fixture(t),calls=[];p.current={path:__filename,size:1234,mtime:10};p.files=[p.current];p.ipc={command:async args=>calls.push(args)}
 await p.configure({preloadBudget:100})
 assert(calls.some(a=>JSON.stringify(a)===JSON.stringify(['set_property','demuxer-max-bytes','512MiB'])))
 assert(calls.some(a=>JSON.stringify(a)===JSON.stringify(['set_property','demuxer-max-back-bytes','256MiB'])))
 assert(!calls.some(a=>['loadfile','seek','stop'].includes(a[0])||a[1]==='hwdec'))
 const restored=new LocalPlayer({storage:p.storage});await restored.ready;assert.equal(restored.settings.preloadBudget,100)
})
test('display-following preference persists and legacy 240 settings migrate on restore',async t=>{
 const p=await fixture(t);await p.configure({danmuFps:0});let restored=new LocalPlayer({storage:p.storage});await restored.ready;assert.equal(restored.settings.danmuFps,0)
 await fs.writeFile(path.join(p.storage,'settings.json'),JSON.stringify({danmuFps:240}));restored=new LocalPlayer({storage:p.storage});await restored.ready;assert.equal(restored.settings.danmuFps,0)
})
