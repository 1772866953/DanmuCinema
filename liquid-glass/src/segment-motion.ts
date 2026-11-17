// Port of TingYe Android 0.5.0 GlassSegmentMotion.kt (MIT).
// CSS pixels replace density-independent Android distances; damping is a ratio,
// not Motion's damping coefficient. One pose owns the raised and resting lens.
const clamp=(x:number,min:number,max:number)=>Math.max(min,Math.min(max,x))
class Spring {
  value:number;target:number;velocity=0;epsilon:number
  constructor(value:number,epsilon:number){this.value=this.target=value;this.epsilon=epsilon}
  step(dt:number,stiffness:number,damping:number){
    const omega=Math.sqrt(stiffness),decay=damping*omega,frequency=omega*Math.sqrt(1-damping*damping)
    const displacement=this.value-this.target,b=(this.velocity+decay*displacement)/frequency
    const c=Math.cos(frequency*dt),s=Math.sin(frequency*dt),e=Math.exp(-decay*dt)
    this.value=this.target+e*(displacement*c+b*s)
    this.velocity=e*((b*frequency-decay*displacement)*c-(displacement*frequency+decay*b)*s)
    if(Math.abs(this.value-this.target)<this.epsilon&&Math.abs(this.velocity)<this.epsilon*12){this.value=this.target;this.velocity=0}
  }
  settled(){return this.value===this.target&&this.velocity===0}
}
export type SegmentFrame={x:number;width:number;y:number;lift:number;dock:number;dockX:number;dockY:number;strain:number;contact:number;stretchX:number;stretchY:number;shear:number}
export class GlassSegmentMotion {
  private position:Spring;private span:Spring
  private vertical=new Spring(0,.025);private contact=new Spring(0,.0005);private elevation=new Spring(0,.0005)
  private dock=new Spring(0,.0005);private dockX=new Spring(0,.025);private dockY=new Spring(0,.025)
  private strain=new Spring(0,.0005);private stretchX=new Spring(0,.0002);private stretchY=new Spring(0,.0002);private shear=new Spring(0,.0002)
  private horizontalPull=0;private verticalPull=0;private pressAge=1;private retractPending=false
  holding=false
  constructor(x:number,width:number){this.position=new Spring(x,.025);this.span=new Spring(width,.025)}
  press(x:number,width:number){
    this.holding=true;this.pressAge=0;this.retractPending=false;this.position.target=x;this.span.target=width
    this.vertical.target=0;this.contact.target=1;this.dockX.target=this.dockY.target=0;this.horizontalPull=this.verticalPull=0
  }
  drag(x:number,width:number,y:number,railWidth:number,overpull:number,pullY=0){
    this.position.target=x;this.span.target=width;this.vertical.target=y
    this.dockX.target=3*Math.tanh((x-railWidth/2)/(railWidth*.45))+overpull*.18;this.dockY.target=y*.5
    this.horizontalPull=overpull/14;this.verticalPull=Math.tanh(pullY/40)
  }
  release(x:number,width:number,minimumPress=true){
    this.holding=false;this.position.target=x;this.span.target=width;this.position.velocity=clamp(this.position.velocity,-900,900)
    this.vertical.target=0;this.retractPending=minimumPress&&this.pressAge<.105
    if(!this.retractPending)this.contact.target=0
    this.dockX.target=this.dockY.target=0;this.horizontalPull=this.verticalPull=0
  }
  selection(x:number,width:number){if(!this.holding){this.position.target=x;this.span.target=width}}
  // Commit on visual landing, not on the last subpixel spring residue. The
  // remaining dock/strain return keeps running without delaying navigation.
  landed(){return !this.holding&&!this.retractPending&&this.elevation.value<=.08&&Math.abs(this.position.value-this.position.target)<1.5&&Math.abs(this.vertical.value)<.7}
  frame():SegmentFrame{
    const a=1+this.stretchX.value,d=1+this.stretchY.value,b=this.shear.value,area=Math.sqrt(Math.max(.5,a*d-b*b))
    return{x:this.position.value,width:this.span.value,y:this.vertical.value,lift:this.elevation.value,dock:this.dock.value,dockX:this.dockX.value,dockY:this.dockY.value,strain:this.strain.value,contact:this.contact.value,stretchX:a/area,stretchY:d/area,shear:b/area}
  }
  step(seconds:number){
    const dt=clamp(seconds,.001,.05),steps=Math.max(1,Math.ceil(dt*240))
    for(let i=0;i<steps;i++)this.integrate(dt/steps)
    return this.retractPending||![this.position,this.span,this.vertical,this.contact,this.elevation,this.dock,this.dockX,this.dockY,this.strain,this.stretchX,this.stretchY,this.shear].every(s=>s.settled())
  }
  private integrate(dt:number){
    this.pressAge+=dt;if(this.retractPending&&this.pressAge>=.105){this.retractPending=false;this.contact.target=0}
    const raised=this.holding||this.retractPending
    this.position.step(dt,this.holding?3000:620,this.holding?.82:.74);this.span.step(dt,900,.78);this.vertical.step(dt,800,.74)
    this.contact.step(dt,raised?900:700,.90);this.elevation.target=this.dock.target=this.contact.value
    this.elevation.step(dt,raised?300:420,raised?.48:.64);this.dock.step(dt,raised?280:300,raised?.64:.70)
    this.dockX.step(dt,this.holding?700:450,.68);this.dockY.step(dt,this.holding?700:450,.68)
    const fx=this.holding?(this.position.target-this.position.value)/18+this.position.velocity/1000+this.horizontalPull*.8:0,fy=this.holding?this.verticalPull:0
    const distance=Math.hypot(fx,fy),amount=1-Math.exp(-distance),ux=distance>.0001?fx/distance:0,uy=distance>.0001?fy/distance:0
    const along=Math.exp(.22*amount),across=1/along
    this.stretchX.target=across+(along-across)*ux*ux-1;this.stretchY.target=across+(along-across)*uy*uy-1
    this.shear.target=clamp((along-across)*ux*uy*.35,-.04,.04)
    if(distance<=.0001)this.stretchX.target=this.stretchY.target=0
    this.strain.target=amount;this.strain.step(dt,300,.72);this.stretchX.step(dt,300,.62);this.stretchY.step(dt,300,.62);this.shear.step(dt,300,.62)
  }
}
