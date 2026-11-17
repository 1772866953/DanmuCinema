// rAF supplies the display phase. A deadline wakeup fills a delayed browser
// BeginFrame without queuing stale draws or tying danmu to video frame rate.
export class FrameClock{
 constructor(draw,{fps=0,displayHz=60,onRaf=()=>{}}={},platform={now:()=>performance.now(),raf:f=>requestAnimationFrame(f),cancelRaf:id=>cancelAnimationFrame(id),timer:(f,ms)=>setTimeout(f,ms),cancelTimer:id=>clearTimeout(id)}){
  this.draw=draw;this.onRaf=onRaf;this.platform=platform;this.running=false;this.raf=0;this.timer=0;this.next=0;this.lastDraw=null;this.configure({fps,displayHz})
 }
 configure({fps,displayHz}={}){
  if(Number.isFinite(displayHz)&&displayHz>=20&&displayHz<=1000)this.displayHz=displayHz
  if(Number.isFinite(fps)&&fps>=0)this.fps=fps
  this.period=1000/Math.min(this.fps||this.displayHz,this.displayHz)
  this.follow=!this.fps||this.fps>=this.displayHz
  this.coupled=this.follow&&(!this.rafPeriod||this.rafPeriod>=this.period*.8)
  this.slack=Math.min(.75,250/this.displayHz)
  this.next=0;this.lastDraw=null;if(this.running)this.arm()
 }
 start(){if(this.running)return;this.running=true;this.next=0;this.lastDraw=null;this.lastRaf=null;this.raf=this.platform.raf(this.onFrame);this.arm()}
 stop(){this.running=false;this.platform.cancelRaf(this.raf);this.platform.cancelTimer(this.timer);this.raf=this.timer=0;this.next=0;this.lastDraw=null}
 onFrame=()=>{
  this.raf=0;if(!this.running)return;const now=this.platform.now();this.onRaf(now)
  if(this.lastRaf!==null){const gap=now-this.lastRaf;if(gap>=this.period*.8&&gap<=this.period*1.2||gap<this.period*.7)this.rafPeriod=gap}
  this.lastRaf=now;this.coupled=this.follow&&(!this.rafPeriod||this.rafPeriod>=this.period*.8)
  if(this.coupled){
   // The OS frequency is rounded (200 Hz can actually be 199.974). Keep the
   // display phase instead of letting two nominal clocks beat against each
   // other. The timer intervenes only when a BeginFrame is late.
   this.next=now+this.period
   if(this.lastDraw===null||now-this.lastDraw>=this.period*.6){this.lastDraw=now;this.draw(now,'raf')}
   if(this.running)this.arm()
  }else this.wake(now,'raf')
  if(this.running)this.raf=this.platform.raf(this.onFrame)
 }
 onTimer=()=>{this.timer=0;if(this.running)this.wake(this.platform.now(),'timer')}
 wake(now,source){
  if(!this.running)return
  if(!this.next||now+this.slack>=this.next){
   const due=this.next||now;this.next=(this.coupled?now:due)+this.period;this.lastDraw=now
   if(this.next<=now)this.next+=Math.floor((now-this.next)/this.period+1)*this.period
   this.draw(now,source)
  }
  if(this.running)this.arm()
 }
 arm(){this.platform.cancelTimer(this.timer);const bias=this.coupled?Math.min(.25,this.period*.05):-Math.min(.5,this.slack);this.timer=this.platform.timer(this.onTimer,Math.max(1,(this.next||this.platform.now()+this.period)+bias-this.platform.now()))}
}
