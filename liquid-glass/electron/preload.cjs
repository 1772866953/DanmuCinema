'use strict'
const {contextBridge,ipcRenderer}=require('electron')
contextBridge.exposeInMainWorld('cinema',{
  call:(method,params)=>ipcRenderer.invoke('core:call',method,params),
  window:action=>ipcRenderer.invoke('window:action',action),
  system:(action,value)=>ipcRenderer.invoke('system:action',action,value),
  onState:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('core:state',fn);return()=>ipcRenderer.removeListener('core:state',fn)},
  onWindow:callback=>{const fn=(_event,value)=>callback(value);ipcRenderer.on('window:state',fn);return()=>ipcRenderer.removeListener('window:state',fn)}
})
