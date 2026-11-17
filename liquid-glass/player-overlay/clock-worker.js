// The animation clock and ASS drawing run on the same OffscreenCanvas worker.
// A frame no longer travels to a worker and back through the UI thread at 200Hz.
import {ASSRenderer} from 'jassub/dist/worker/worker.js'
import {FrameClock} from './frame-clock.js'
import {GPUQueue} from './gpu-queue.js'
import {installAtlasRing} from './ass-gpu-adapter.js'
// Small overlay frames should not wake eight parallel raster workers at every
// vblank. This changes the overlay's libass scheduling only; mpv is untouched.
const serialRendering=import.meta.env.VITE_DANMU_ASS_SERIAL!=='0'
const deadlineRendering=import.meta.env.VITE_DANMU_OVERLAY_DEADLINE!=='0'
const asyncRendering=import.meta.env.VITE_DANMU_ASS_ASYNC_GPU!=='0'
const constructor=Symbol.for('constructor'),initialize=ASSRenderer.prototype[constructor]
ASSRenderer.prototype[constructor]=async function(...args){
 const result=await initialize.apply(this,args);if(serialRendering)this._wasm.setThreads(1)
 const renderer=this._gpurender,gl=renderer.gl
 if(asyncRendering&&gl?.fenceSync){
  const queue=this.cinemaGPU=new GPUQueue(gl,3);installAtlasRing(renderer,queue,this)
 }
 return result
}
const draw=ASSRenderer.prototype._draw
ASSRenderer.prototype._draw=function(time,repaint=false){
 if(this.cinemaGPU&&!this.cinemaGPU.ready()){
  this.cinemaRepaint=true
  if(this.cinemaPendingDraw&&!this.cinemaDrawRetrying)this.cinemaGPU.discard()
  this.cinemaPendingDraw={time}
  // A fence may become ready within this same display interval. Yield instead
  // of blocking the worker or throwing the whole interval away. Keep one latest
  // request, and recompute moving danmu at the retry time, never a stale pose.
  if(!this.cinemaPendingTimer&&!this.cinemaGPU.gl.isContextLost())this.cinemaPendingTimer=setTimeout(()=>{
   this.cinemaPendingTimer=0;const pending=this.cinemaPendingDraw;if(!pending||!this._wasm||!this._gpurender)return
   this.cinemaDrawRetrying=true
   try{if(this.cinemaClock?.tick)this.cinemaClock.tick(performance.now(),'retry');else this._draw(pending.time,true)}finally{this.cinemaDrawRetrying=false}
  },1)
  return false
 }
 this.cancelGPUWork()
 const dirty=this.cinemaRepaint;this.cinemaRepaint=false;draw.call(this,time,repaint||dirty);return true
}
ASSRenderer.prototype.cancelGPUWork=function(){if(this.cinemaPendingTimer)clearTimeout(this.cinemaPendingTimer);this.cinemaPendingTimer=0;this.cinemaPendingDraw=null}
ASSRenderer.prototype.renderQueueStats=function(reset=false){return this.cinemaGPU?.snapshot(reset)||{submitted:0,skipped:0,peak:0,pending:0,failures:0}}
const fresh=()=>({draws:0,paints:0,times:[],rafTimes:[],costs:[],lateFrames:[],errors:[],coalesced:0,clockBacksteps:0,maxClockBackstep:0,maxClockStepError:0,timerDraws:0,frameIntervals:0,frameMax:0,frameMisses:0,lastPaintAt:null})
ASSRenderer.prototype.startClock=function(sample={}){
 this.stopClock?.()
 const state=this.cinemaClock={anchor:sample.time||0,at:performance.now(),clock:sample.time||0,last:performance.now(),speed:sample.speed||1,paused:!!sample.paused,offset:sample.offset||0,fps:sample.fps||0,displayHz:sample.displayHz||60,deadline:0,transportUntil:0,stats:fresh(),recording:false,frame:0}
 if(!this.cinemaRenderWrapped){const render=this._gpurender.render.bind(this._gpurender);this._gpurender.render=(...args)=>{if(this.cinemaClock)this.cinemaClock.stats.paints++;return render(...args)};this.cinemaRenderWrapped=true}
 const tick=(now,source)=>{
  if(this.cinemaClock!==state)return
  const dt=Math.max(0,now-state.last)/1000,previous=state.clock;state.last=now;if(!deadlineRendering&&state.recording&&state.stats.rafTimes.length<12000)state.stats.rafTimes.push(now)
  if(state.paused)state.clock=state.anchor
  else{
   const predicted=state.clock+dt*state.speed,target=state.anchor+(now-state.at)/1000*state.speed,transport=now<state.transportUntil,limit=dt*state.speed*(transport?.7:.03),error=(target-predicted)*(transport?1-Math.exp(-dt/.06):1)
   state.clock=predicted+Math.max(-limit,Math.min(limit,error))
  }
  if(state.recording&&!state.paused){const step=state.clock-previous;if(step<0){state.stats.clockBacksteps++;state.stats.maxClockBackstep=Math.max(state.stats.maxClockBackstep,-step)}state.stats.maxClockStepError=Math.max(state.stats.maxClockStepError,Math.abs(step-dt*state.speed))}
  if(deadlineRendering||!state.fps||now>=state.deadline){
   if(!deadlineRendering&&state.fps){const interval=1000/state.fps;state.deadline=state.deadline?state.deadline+interval:now+interval;if(state.deadline<=now)state.deadline+=Math.ceil((now-state.deadline)/interval)*interval}
   if(source==='timer')state.stats.timerDraws++
   const began=performance.now()
   try{if(this._draw(Math.max(0,state.clock-state.offset),source==='paused')!==false){state.stats.draws++;if(state.recording){const at=performance.now(),stats=state.stats;if(stats.lastPaintAt!==null){const gap=at-stats.lastPaintAt;stats.frameIntervals++;stats.frameMax=Math.max(stats.frameMax,gap);if(gap>1500/Math.min(state.fps||state.displayHz,state.displayHz)){stats.frameMisses++;if(stats.lateFrames.length<64)stats.lateFrames.push({at,gap,source,cost:at-began,rafAge:state.scheduler?at-state.scheduler.lastRaf:null});console.timeStamp?.('danmu-late:'+at+':'+gap)}}stats.lastPaintAt=at;if(stats.times.length<12000){stats.times.push(at);stats.costs.push(at-began)}}}}catch(error){if(state.stats.errors.length<64)state.stats.errors.push(String(error))}
  }
  if(!deadlineRendering)state.frame=requestAnimationFrame(tick)
 }
 if(deadlineRendering){state.tick=tick;state.scheduler=new FrameClock(tick,{fps:state.fps,displayHz:state.displayHz,onRaf:now=>{if(state.recording&&state.stats.rafTimes.length<12000)state.stats.rafTimes.push(now)}});if(!state.paused)state.scheduler.start();else tick(performance.now(),'paused')}
 else state.frame=requestAnimationFrame(tick)
}
ASSRenderer.prototype.sampleClock=function(sample){
 const s=this.cinemaClock;if(!s)return
 const now=performance.now(),wasPaused=s.paused;if(Math.abs(s.speed-sample.speed)>.00001)s.transportUntil=now+1200
 Object.assign(s,{anchor:sample.time,at:now,speed:sample.speed,paused:sample.paused})
 if(s.paused!==wasPaused)s.stats.lastPaintAt=null
 if(sample.reset){s.clock=s.anchor;s.last=now}
 if(s.scheduler){if(s.paused){s.scheduler.stop();if(!wasPaused||sample.reset)s.tick(now,'paused')}else if(wasPaused){s.last=now;s.scheduler.start()}}
}
ASSRenderer.prototype.configureClock=function(value){const s=this.cinemaClock;if(s){const changed=value.offset!==undefined&&s.offset!==value.offset;if(value.fps!==undefined)s.fps=value.fps||0;if(value.offset!==undefined)s.offset=value.offset||0;if(value.displayHz!==undefined)s.displayHz=value.displayHz;s.deadline=0;s.scheduler?.configure({fps:s.fps,displayHz:s.displayHz});if(changed&&s.paused)s.tick?.(performance.now(),'paused')}}
ASSRenderer.prototype.redrawClock=function(){const s=this.cinemaClock;if(s?.paused)s.tick?.(performance.now(),'paused')}
ASSRenderer.prototype.clockStats=function(reset=false){
 const s=this.cinemaClock;if(!s)return fresh()
 const target=s.anchor+(s.paused?0:(performance.now()-s.at)/1000*s.speed),result={...s.stats,clock:s.clock,target,drift:s.clock-target,requestedFps:s.fps,gpu:this.renderQueueStats(reset)}
 if(reset){s.stats=fresh();s.recording=true;console.timeStamp?.('danmu-probe-reset')}
 return result
}
ASSRenderer.prototype.stopClock=function(){this.cancelGPUWork();if(this.cinemaClock){cancelAnimationFrame(this.cinemaClock.frame);this.cinemaClock.scheduler?.stop();this.cinemaClock=null}}
ASSRenderer.prototype.diagnoseClock=function(){
 const state=this.cinemaClock,capture=this._drawCapture(state?.clock||20)
 let alphaPixels=0;if(capture)for(let i=3;i<capture.data.length;i+=4)if(capture.data[i]>20)alphaPixels++
 return {width:capture?.width,height:capture?.height,alphaPixels,lastDraw:this._lastDrawTime,events:this.getEvents().length,clock:state?.clock,paints:state?.stats.paints,serialRendering,deadlineRendering,asyncRendering,contextAttributes:this._gpurender.gl?.getContextAttributes(),displayHz:state?.displayHz}
}
