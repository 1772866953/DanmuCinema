'use strict'
const {EventEmitter}=require('node:events'),{spawn}=require('node:child_process'),path=require('node:path'),fs=require('node:fs/promises'),net=require('node:net'),crypto=require('node:crypto'),{StringDecoder}=require('node:string_decoder')
// A separate renderer/GPU process keeps UI composition off the video path and
// lets all of its memory go away when playback closes.
class PlayerOverlay extends EventEmitter{
 constructor({base,runtime,scripts,storage,settings,packaged=false,executable=process.execPath,spawnOverlay=spawn}){
  super();Object.assign(this,{base,runtime,scripts,storage,settings,packaged});this.pending=new Map();this.sequence=0;this.buffer='';this.decoder=new StringDecoder('utf8');this.ending=false
  this.ready=new Promise((resolve,reject)=>{this.resolveReady=resolve;this.rejectReady=reject})
  this.exit=new Promise(resolve=>this.resolveExit=resolve)
  const id=crypto.randomUUID(),transport=process.platform==='win32'?'\\\\.\\pipe\\DanmuCinemaOverlay-'+id:path.join(storage,'overlay.sock'),token=crypto.randomBytes(32).toString('hex')
  this.server=net.createServer(socket=>{
   let buffer=Buffer.alloc(0),authenticated=false
   socket.setTimeout(5000,()=>{if(!authenticated)socket.destroy()})
   socket.on('error',()=>{})
   socket.on('data',data=>{
    if(authenticated){this.read(data);return}
    buffer=Buffer.concat([buffer,data]);const split=buffer.indexOf(10);if(split<0){if(buffer.length>4096)socket.destroy();return}
    if(split>4096){socket.destroy();return}
    let hello;try{hello=JSON.parse(buffer.subarray(0,split).toString('utf8'))}catch{socket.destroy();return}
    if(hello.token!==token||this.socket){socket.destroy();return}
    authenticated=true;socket.setTimeout(0);this.socket=socket;this.server.close();if(buffer.length>split+1)this.read(buffer.subarray(split+1));buffer=null
   })
   socket.on('close',()=>{if(authenticated&&!this.ending)this.fail(new Error('播放界面连接已断开，请重新打开影片。'))})
  })
  this.server.once('error',error=>{this.rejectAll(error);if(!this.child)this.resolveExit({error})})
  this.server.listen(transport,()=>{
   if(this.ending){this.server.close();this.resolveExit({code:0});return}
   const env={...process.env,DANMU_OVERLAY_CONFIG:JSON.stringify({base,runtime,storage,settings,parentPid:process.pid,transport,token})};delete env.ELECTRON_RUN_AS_NODE
   this.child=spawnOverlay(executable,[...(packaged?[]:[base]),'--player-overlay'],{env,windowsHide:true,stdio:['ignore','ignore','pipe']})
   this.child.once('exit',(code,signal)=>{this.server.close();this.socket?.destroy();this.fail(new Error('播放界面绘制进程已停止，请重新打开影片。'));this.resolveExit({code,signal})})
   this.child.once('error',error=>{this.server.close();this.rejectAll(error);this.resolveExit({error})});let bytes=0
   this.child.stderr.on('data',data=>{if((bytes+=data.length)<1024*1024)void fs.appendFile(path.join(storage,'overlay.log'),data).catch(()=>{})})
  })
  this.startTimer=setTimeout(()=>this.rejectAll(new Error('播放界面初始化超时。')),15000);this.startTimer.unref()
 }
 fail(error){this.rejectAll(error);if(!this.ending&&this.initialized&&!this.faulted){this.faulted=true;this.emit('fault',error)}}
 read(data){this.buffer+=this.decoder.write(data);if(this.buffer.length>1024*1024){this.rejectAll(new Error('播放界面返回数据过大。'));return}let split
  while((split=this.buffer.indexOf('\n'))>=0){const line=this.buffer.slice(0,split);this.buffer=this.buffer.slice(split+1);let message;try{message=JSON.parse(line)}catch{continue}
   if(message.event==='ready'){clearTimeout(this.startTimer);this.hwnd=message.hwnd;this.pid=message.pid;this.initialized=true;this.resolveReady();continue}
   if(message.event==='mpv-closed'){this.ending=true;continue}
   const pending=this.pending.get(message.id);if(pending){clearTimeout(pending.timer);this.pending.delete(message.id);message.error?pending.reject(new Error(message.error)):pending.resolve(message.value)}
  }
 }
 get args(){return ['--script='+path.join(this.scripts,'cinema-host.lua'),'--script-opts-append=cinema-external=yes','--script-opts-append=cinema_host-hwnd='+this.hwnd,'--script-opts-append=cinema_host-parent_pid='+this.pid,'--script-opts-append=cinema_host-hidden_until_loaded=yes']}
 request(method,value,timeout=12000){if(this.ending&&method!=='quit')return Promise.reject(new Error('播放界面正在关闭。'));if(!this.socket||this.socket.destroyed)return Promise.reject(new Error('播放界面尚未连接。'));return new Promise((resolve,reject)=>{const id=++this.sequence,timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('播放界面操作超时。'))},timeout);this.pending.set(id,{resolve,reject,timer});this.socket.write(JSON.stringify({id,method,value})+'\n',error=>{if(error){clearTimeout(timer);this.pending.delete(id);reject(error)}})})}
 rejectAll(error){clearTimeout(this.startTimer);this.rejectReady(error);for(const pending of this.pending.values()){clearTimeout(pending.timer);pending.reject(error)}this.pending.clear()}
 attach(pipe){return this.request('attach',{pipe})}
 prepare(){return this.request('prepare')}
 configure(settings){return this.request('settings',settings)}
 scene(file){return this.request('scene',{file})}
 clear(){return this.request('clear')}
 probe(reset=false){return this.request('probe',{reset})}
 diagnose(){return this.request('diagnose')}
 capture(){return this.request('capture')}
 async close(){if(this.closing)return this.closing;this.ending=true;this.closing=(async()=>{clearTimeout(this.startTimer);await this.request('quit',null,1000).catch(()=>{});await Promise.race([this.exit,new Promise(r=>setTimeout(r,1500))]);if(this.child?.exitCode===null)this.child.kill();this.server.close();this.socket?.destroy();this.rejectAll(new Error('播放界面已关闭。'))})();return this.closing}
}
module.exports={PlayerOverlay}
