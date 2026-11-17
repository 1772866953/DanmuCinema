'use strict'
const net=require('node:net'),{EventEmitter}=require('node:events'),{StringDecoder}=require('node:string_decoder')
class MpvIpc extends EventEmitter{
 constructor(){super();this.sequence=0;this.pending=new Map();this.buffer='';this.decoder=new StringDecoder('utf8');this.closed=false}
 async connect(pipe,timeout=12000){const end=Date.now()+timeout;while(!this.closed&&Date.now()<end){try{await new Promise((resolve,reject)=>{const socket=net.createConnection(pipe);const fail=e=>{socket.destroy();reject(e)};socket.once('error',fail);socket.once('connect',()=>{socket.removeListener('error',fail);this.socket=socket;socket.on('data',data=>this.read(data));socket.on('error',()=>{});socket.on('close',()=>this.disconnect(new Error('播放连接已关闭。')));resolve()})});return}catch{await new Promise(resolve=>setTimeout(resolve,60))}}throw new Error('无法连接内置播放内核，请查看本地播放日志。')}
 read(data){this.buffer+=this.decoder.write(data);if(this.buffer.length>8*1024*1024){this.close();return}let n;while((n=this.buffer.indexOf('\n'))>=0){const line=this.buffer.slice(0,n);this.buffer=this.buffer.slice(n+1);try{const msg=JSON.parse(line);if(msg.request_id){const p=this.pending.get(msg.request_id);if(p){clearTimeout(p.timer);this.pending.delete(msg.request_id);msg.error==='success'?p.resolve(msg.data):p.reject(new Error('播放器：'+msg.error))}}else if(msg.event)this.emit('event',msg)}catch{}}}
 command(command,timeout=5000){if(!this.socket||this.socket.destroyed||this.closed)return Promise.reject(new Error('当前没有播放会话。'));return new Promise((resolve,reject)=>{const id=++this.sequence,timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('播放器操作超时。'))},timeout);this.pending.set(id,{resolve,reject,timer});this.socket.write(JSON.stringify({command,request_id:id})+'\n',error=>{if(error){clearTimeout(timer);this.pending.delete(id);reject(error)}})})}
 disconnect(error){if(this.closed)return;this.closed=true;for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(error)}this.pending.clear();this.emit('disconnect')}
 close(){this.disconnect(new Error('播放会话已结束。'));this.socket?.destroy()}
}
module.exports={MpvIpc}
