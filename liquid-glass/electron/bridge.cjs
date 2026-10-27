'use strict'
const { spawn } = require('node:child_process')
const readline = require('node:readline')
const { EventEmitter } = require('node:events')
class CoreBridge extends EventEmitter {
  constructor(executable, env) {
    super(); this.pending = new Map(); this.sequence = 0
    this.child = spawn(executable,['--electron-bridge'],{env:{...process.env,...env},windowsHide:true,stdio:['pipe','pipe','pipe']})
    this.ready = new Promise((resolve,reject)=>{this.resolveReady=resolve;this.rejectReady=reject})
    this.child.stderr.on('data',()=>{})
    readline.createInterface({input:this.child.stdout}).on('line',line=>{
      try { const m=JSON.parse(line)
        if(m.type==='ready'){this.resolveReady(m.data);this.emit('state',m.data)}
        else if(m.type){this.emit(m.type,m.data??m)}
        else {const p=this.pending.get(m.id);if(p){clearTimeout(p.timer);this.pending.delete(m.id);m.error?p.reject(new Error(m.error)):p.resolve(m.result)}}
      } catch(error){this.emit('protocol-error',error.message)}
    })
    const fail = error=>{this.rejectReady(error);for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(error)}this.pending.clear();this.emit('offline',error.message)}
    this.child.on('error',fail);this.child.on('exit',code=>fail(new Error(`媒体核心已退出 (${code})`)))
  }
  call(method,params={}) {return new Promise((resolve,reject)=>{const id=String(++this.sequence);const timer=setTimeout(()=>{this.pending.delete(id);reject(new Error('操作超时，请检查运行日志。'))},180000);this.pending.set(id,{resolve,reject,timer});this.child.stdin.write(JSON.stringify({id,method,params})+'\n',error=>{if(error){clearTimeout(timer);this.pending.delete(id);reject(error)}})})}
  async close(){await this.call('shutdown');this.child.stdin.end()}
}
module.exports={CoreBridge}
