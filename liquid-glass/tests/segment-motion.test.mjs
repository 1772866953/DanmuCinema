import test from 'node:test'
import assert from 'node:assert/strict'
import {GlassSegmentMotion} from '../src/segment-motion.ts'
const advance=(m,seconds,hz=60)=>{for(let i=0;i<Math.round(seconds*hz);i++)m.step(1/hz)}
const near=(a,b,e=.001)=>assert(Math.abs(a-b)<=e,`${a} != ${b}`)
test('selection acknowledgement preserves the single moving body',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(40,70);advance(m,.3);m.drag(260,70,0,320,0);advance(m,.3);m.release(280,70)
 for(let i=0;i<30;i++){const before=m.frame();m.selection(280,70);assert.deepEqual(m.frame(),before);m.step(1/60);assert(m.frame().x>250)}
 advance(m,1);near(m.frame().x,280)
})
test('dock expands six percent, returns with bounded inertia, then sleeps',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(40,70);advance(m,.4);assert(1+.06*m.frame().dock>1.055);m.release(40,70)
 let min=1;for(let i=0;i<90;i++){m.step(1/60);min=Math.min(min,m.frame().dock)}
 assert(min<0&&min>-.08);assert.equal(m.frame().dock,0);assert.equal(m.step(1/60),false)
})
test('reversing and repressing retains pose and velocity without a reset',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(280,70);advance(m,.08);m.drag(250,70,4,320,8);advance(m,.04)
 const before=m.frame();m.release(280,70);assert.deepEqual(m.frame(),before);m.step(1/60);const returning=m.frame();m.press(40,70);assert.deepEqual(m.frame(),returning)
 advance(m,.3);assert(m.frame().x<45);m.release(40,70);advance(m,2);assert.equal(m.frame().x,40);assert.equal(m.frame().y,0)
})
test('60 and 120 Hz preserve the Android response',()=>{
 const sample=hz=>{const m=new GlassSegmentMotion(40,70);m.press(280,100);advance(m,.2,hz);m.release(280,100);advance(m,.1,hz);return m.frame()}
 const a=sample(60),b=sample(120);near(a.x,b.x,.01);near(a.width,b.width,.01);near(a.dock,b.dock);near(a.lift,b.lift)
})
test('press and release within one frame still generate one visible pulse',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(280,70);m.release(280,70);let dock=0,lift=0
 for(let i=0;i<90;i++){m.step(1/60);dock=Math.max(dock,m.frame().dock);lift=Math.max(lift,m.frame().lift)}
 assert(dock>.4&&lift>.4);assert.equal(m.frame().x,280);assert.equal(m.step(1/60),false)
})
test('contact leads the heavier dock and lens; held lens has a bounded tremor',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(40,70);m.step(1/60);assert(m.frame().contact>.05&&m.frame().lift<.02)
 let peak=0,returned=false;for(let i=0;i<180;i++){m.step(1/120);peak=Math.max(peak,m.frame().lift);if(peak>1.02&&m.frame().lift<1)returned=true}
 assert(peak>1.02&&peak<1.16&&returned);near(m.frame().lift,1);assert.equal(m.step(1/60),false)
})
test('horizontal and vertical forces deform different axes',()=>{
 const h=new GlassSegmentMotion(40,70);h.press(40,70);advance(h,.5);h.drag(40,70,0,320,-14);advance(h,.4);assert(h.frame().stretchX>1.04&&h.frame().stretchY<.97)
 const v=new GlassSegmentMotion(40,70);v.press(40,70);advance(v,.5);v.drag(40,70,6,320,0,120);advance(v,.4);assert(v.frame().stretchY>1.04&&v.frame().stretchX<.97)
})
test('oblique reversal preserves area without rotation or instability',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(40,70)
 for(let i=0;i<=100;i++){const d=i<50?1:-1;m.drag(40+d*50,70,d*18,320,d*54,d*160);m.step(1/120);const f=m.frame();near(f.stretchX*f.stretchY-f.shear*f.shear,1,.0001);assert(f.stretchX>.78&&f.stretchX<1.28&&Math.abs(f.shear)<.045)}
 const before=m.frame();m.release(40,70);assert.deepEqual(m.frame(),before);advance(m,2);assert.equal(m.frame().stretchX,1);assert.equal(m.frame().stretchY,1);assert.equal(m.step(1/60),false)
})
test('scroll cancellation does not finish a false press pulse',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(40,70);m.step(1/120);m.release(40,70,false);let peak=0
 for(let i=0;i<120;i++){m.step(1/120);peak=Math.max(peak,m.frame().dock)}assert(peak<.08);advance(m,1);assert.equal(m.step(1/60),false)
})
test('visual landing commits promptly while the small return still runs',()=>{
 const m=new GlassSegmentMotion(40,70);m.press(280,70);advance(m,.4);assert.equal(m.landed(),false);m.release(280,70)
 let time=0,more=true;while(!m.landed()&&time<1){more=m.step(1/120);time+=1/120}
 assert(time>.10&&time<.4,`landing ${time}s`);assert(more,'navigation must not wait for the full settle');assert(m.frame().lift<=.08)
 advance(m,2);assert(m.landed());assert.equal(m.step(1/60),false)
})
