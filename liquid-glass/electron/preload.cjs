'use strict'
const {contextBridge,ipcRenderer}=require('electron')
contextBridge.exposeInMainWorld('cinema',{
  call:(method,params)=>ipcRenderer.invoke('core:call',method,params),
  window:action=>ipcRenderer.invoke('window:action',action),
  system:(action,value)=>ipcRenderer.invoke('system:action',action,value),
  player:(action,value)=>ipcRenderer.invoke('player:action',action,value),
  onPlayer:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('player:state',fn);return()=>ipcRenderer.removeListener('player:state',fn)},
  onPlayerFault:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('player:fault',fn);return()=>ipcRenderer.removeListener('player:fault',fn)},
  onState:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('core:state',fn);return()=>ipcRenderer.removeListener('core:state',fn)},
  onWindow:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('window:state',fn);return()=>ipcRenderer.removeListener('window:state',fn)}
})
