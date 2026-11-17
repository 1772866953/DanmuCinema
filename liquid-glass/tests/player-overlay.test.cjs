const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path'),net=require('node:net'),{EventEmitter,once}=require('node:events'),{PassThrough}=require('node:stream')
const {PlayerOverlay}=require('../electron/overlay-host.cjs')
const tick=()=>new Promise(resolve=>setImmediate(resolve))
async function fixture(t,handler=()=>{},startup={}){
 const storage=await fs.mkdtemp(path.join(os.tmpdir(),'DanmuCinema-overlay-unit-'));let socket,config
 const child=new EventEmitter();child.stderr=new PassThrough();child.exitCode=null
 const exit=()=>{if(child.exitCode!==null)return;child.exitCode=0;socket?.destroy();child.emit('exit',0)};child.kill=exit
 const host=new PlayerOverlay({base:storage,runtime:storage,scripts:storage,storage,settings:{},spawnOverlay:(_,args,options)=>{
  config=JSON.parse(options.env.DANMU_OVERLAY_CONFIG);assert.equal(options.stdio[0],'ignore');assert(args.includes('--player-overlay'))
  socket=net.createConnection(config.transport);socket.on('error',()=>{})
  socket.once('connect',()=>{socket.write(JSON.stringify({token:config.token})+'\n');if(!startup.wait)socket.write(JSON.stringify({event:'ready',hwnd:'1234',pid:123})+'\n')})
  let buffer='';socket.on('data',data=>{buffer+=data;let split;while((split=buffer.indexOf('\n'))>=0){const message=JSON.parse(buffer.slice(0,split));buffer=buffer.slice(split+1);if(message.method==='quit'){socket.write(JSON.stringify({id:message.id,value:true})+'\n',exit)}else handler(message,socket)}})
  return child
 }})
 host.ready.catch(()=>{})
 t.after(async()=>{await host.close();socket?.destroy();child.stderr.destroy();assert(path.dirname(storage)===os.tmpdir()&&path.basename(storage).startsWith('DanmuCinema-overlay-unit-'));await fs.rm(storage,{recursive:true,force:true})})
 if(!startup.wait)await host.ready
 return {host,child,get socket(){return socket},get config(){return config},exit}
}
test('overlay uses an authenticated local pipe and survives stdin being absent',async t=>{
 const {host}=await fixture(t,(message,socket)=>socket.write(JSON.stringify({id:message.id,value:{pipe:message.value.pipe}})+'\n'))
 assert.deepEqual(await host.attach('mpv-pipe'),{pipe:'mpv-pipe'});assert(host.args.includes('--script-opts-append=cinema-external=yes'));assert(host.args.includes('--script-opts-append=cinema_host-hwnd=1234'))
})
test('split UTF-8 replies retain Chinese text and independent request IDs',async t=>{
 const {host}=await fixture(t,(message,socket)=>{const data=Buffer.from(JSON.stringify({id:message.id,value:message.method==='probe'?'第一集 😀':'第二集'})+'\n');for(const byte of data)socket.write(Buffer.from([byte]))})
 assert.deepEqual(await Promise.all([host.probe(),host.diagnose()]),['第一集 😀','第二集'])
})
test('disconnect rejects pending work and reports one fault even when the process also exits',async t=>{
 const f=await fixture(t),faults=[];f.host.on('fault',e=>faults.push(e));const pending=f.host.probe();const rejected=assert.rejects(pending,/断开|停止/);f.exit();await rejected;await tick();assert.equal(faults.length,1);assert.equal(f.host.pending.size,0)
})
test('normal mpv exit and repeated close do not publish a fault',async t=>{
 const f=await fixture(t),faults=[];f.host.on('fault',e=>faults.push(e));f.socket.write(JSON.stringify({event:'mpv-closed'})+'\n');await tick();await Promise.all([f.host.close(),f.host.close()]);assert.equal(faults.length,0);assert.equal(f.host.child.exitCode,0)
})
test('closing an initializing overlay cancels startup without leaving a child or listener',async t=>{
 const f=await fixture(t,()=>{},{wait:true});while(!f.socket)await tick();if(f.socket.readyState!=='open')await once(f.socket,'connect');await f.host.close();await assert.rejects(f.host.ready,/关闭|停止/);assert.equal(f.child.exitCode,0);assert.equal(f.host.server.listening,false)
})
test('an unauthenticated peer cannot complete startup or consume the pipe',async t=>{
 const f=await fixture(t,()=>{},{wait:true});while(!f.config)await tick();const intruder=net.createConnection(f.config.transport);intruder.on('error',()=>{});intruder.on('connect',()=>intruder.write(JSON.stringify({token:'wrong',event:'ready',hwnd:'666',pid:666})+'\n'));await once(intruder,'close');assert(!f.host.initialized);f.socket.write(JSON.stringify({event:'ready',hwnd:'1234',pid:123})+'\n');await f.host.ready;assert.equal(f.host.hwnd,'1234')
})
