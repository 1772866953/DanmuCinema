'use strict'
const {contextBridge,ipcRenderer}=require('electron')
const subscribe=(channel,callback)=>ipcRenderer.on(channel,(_,value)=>callback(value))
contextBridge.exposeInMainWorld('overlayHost',{
 bootstrap:()=>ipcRenderer.invoke('overlay-bootstrap'),
 onClock:callback=>subscribe('overlay-clock',callback),onUI:callback=>subscribe('overlay-ui',callback),onGeometry:callback=>subscribe('overlay-geometry',callback),
 onDisplay:callback=>subscribe('overlay-display',callback),
 onScene:callback=>subscribe('overlay-scene',callback),onSettings:callback=>subscribe('overlay-settings',callback),ready:()=>ipcRenderer.send('overlay-ready')
})
