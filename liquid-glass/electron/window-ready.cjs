'use strict'
// Readiness belongs to this exact window and its completed GPU frame. A late
// reply from a destroyed/restored predecessor must not reveal another window.
module.exports=function createWindowReady({isCurrent,onReady,beforeCapture}){
 const preparing=new WeakMap()
 return function ready(window){
  if(!preparing.has(window))preparing.set(window,(async()=>{
   if(window.isDestroyed()||!isCurrent(window))return
   if(beforeCapture)await beforeCapture(window)
   if(window.isDestroyed()||!isCurrent(window))return
   await window.webContents.capturePage(undefined,{stayHidden:true,stayAwake:true})
   if(!window.isDestroyed()&&isCurrent(window))onReady(window)
  })())
  return preparing.get(window)
 }
}
