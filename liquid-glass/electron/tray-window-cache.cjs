'use strict'
// Keep only the most recently hidden window for a bounded interval. Expiry
// releases its renderer and GPU resources; no hidden renderer is kept forever.
function createTrayWindowCache({duration,getCurrent,release,schedule=setTimeout,cancel=clearTimeout}){
 let parked=null,timer=null
 function clear(){if(timer!==null)cancel(timer);timer=null;parked=null}
 return{
  park(window){
   if(duration<=0||window.isDestroyed())return false
   clear();const lease={window};parked=lease
   timer=schedule(()=>{
    if(parked!==lease)return
    clear()
    if(getCurrent()===window&&!window.isDestroyed()&&!window.isVisible())release(window)
   },duration)
   timer?.unref?.();window.hide();return true
  },
  resume(window){if(parked?.window!==window)return false;clear();return true},
  forget(window){if(parked?.window===window)clear()},
  isParked(window){return parked?.window===window},
  dispose:clear
 }
}
module.exports={createTrayWindowCache}
