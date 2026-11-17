const {test}=require('node:test'),assert=require('node:assert/strict')
function simulation(FrameClock,{fps=0,hz=200,rafHz=hz,stall=()=>false}={}){
 let now=0,id=0;const jobs=new Map(),draws=[]
 const add=(callback,at)=>{const key=++id;jobs.set(key,{callback,at});return key}
 const platform={now:()=>now,raf:callback=>{let at=(Math.floor((now+.00001)/(1000/rafHz))+1)*1000/rafHz;while(stall(at))at+=1000/rafHz;return add(callback,at)},cancelRaf:key=>jobs.delete(key),timer:(callback,delay)=>add(callback,now+delay),cancelTimer:key=>jobs.delete(key)}
 const clock=new FrameClock((at,source)=>draws.push({at,source}),{fps,displayHz:hz},platform)
 return{clock,draws,jobs,advance(end){let turns=0;while(jobs.size){const [key,job]=[...jobs].sort((a,b)=>a[1].at-b[1].at)[0];if(job.at>end)break;assert(++turns<100000,'scheduler must not spin');jobs.delete(key);now=job.at;job.callback(now)}now=end}}
}
const load=()=>import('../player-overlay/frame-clock.js')
test('delayed browser BeginFrame does not stop overlay submissions',async()=>{const {FrameClock}=await load(),s=simulation(FrameClock,{stall:t=>t>=50&&t<80});s.clock.start();s.advance(150);const gaps=s.draws.slice(1).map((d,i)=>d.at-s.draws[i].at);assert(Math.max(...gaps)<=6.0);assert(s.draws.some(d=>d.source==='timer'&&d.at>50&&d.at<80));s.clock.stop();assert.equal(s.jobs.size,0)})
test('RAF and deadline arriving together never double-submit',async()=>{const {FrameClock}=await load(),s=simulation(FrameClock);s.clock.start();s.advance(1000);assert(s.draws.length>=199&&s.draws.length<=201);for(let i=1;i<s.draws.length;i++)assert(s.draws[i].at-s.draws[i-1].at>=4);s.clock.stop()})
test('60 and 120 Hz requests retain their cadence on a 200 Hz display',async()=>{const {FrameClock}=await load();for(const fps of [60,120]){const s=simulation(FrameClock,{fps});s.clock.start();s.advance(1000);assert(Math.abs(s.draws.length-fps)<=1);assert(Math.max(...s.draws.slice(1).map((d,i)=>d.at-s.draws[i].at))<1500/fps);s.clock.stop()}})
test('monitor change updates follow-display rate without a stale timer',async()=>{const {FrameClock}=await load(),s=simulation(FrameClock);s.clock.start();s.advance(300);s.clock.configure({displayHz:60});const start=s.draws.length;s.advance(1300);assert(Math.abs(s.draws.length-start-60)<=1);s.clock.stop();s.advance(2000);assert.equal(s.jobs.size,0)})
test('stop and repeated start leave one active clock',async()=>{const {FrameClock}=await load(),s=simulation(FrameClock);s.clock.start();s.clock.start();s.advance(100);const count=s.draws.length;s.clock.stop();s.advance(500);assert.equal(s.draws.length,count);s.clock.start();s.advance(600);assert(Math.abs(s.draws.length-count-20)<=1);s.clock.stop();assert.equal(s.jobs.size,0)})

test('rounded monitor rate does not beat against the actual vblank clock',async()=>{const {FrameClock}=await load(),s=simulation(FrameClock,{hz:200,rafHz:199.974});s.clock.start();s.advance(60000);assert(Math.abs(s.draws.length-199.974*60)<=1);assert(s.draws.every(d=>d.source==='raf'));assert(Math.max(...s.draws.slice(1).map((d,i)=>d.at-s.draws[i].at))<5.01);s.clock.stop()})
