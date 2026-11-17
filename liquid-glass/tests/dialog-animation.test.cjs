const {test}=require('node:test'),assert=require('node:assert/strict')
test('native dialog keyframes preserve the original per-property Motion spring',async()=>{
 const {dialogSpringFrames}=await import('../src/dialog-animation.ts'),{spring}=await import('motion')
 const source={scale:.94,y:16,opacity:0},target={scale:1,y:0,opacity:1},{frames,duration}=dialogSpringFrames(source,target)
 assert(duration>200&&duration<1000);assert.deepEqual(frames[0],{transform:'translateY(16px) scale(0.94)',opacity:0});assert.deepEqual(frames.at(-1),{transform:'translateY(0px) scale(1)',opacity:1})
 const reference=spring({keyframes:[16,0],stiffness:390,damping:34,mass:.8});for(let i=0;i<frames.length;i++){const value=reference.next(i*1000/240).value;assert(frames[i].transform.startsWith(`translateY(${value}px)`));assert(Number.isFinite(Number(frames[i].opacity)))}
})
test('interrupted opening samples the current pose rather than resetting to the original bounds',async()=>{
 const {dialogSpringFrames}=await import('../src/dialog-animation.ts'),frames=dialogSpringFrames({scale:.98,y:4,opacity:.6},{scale:1,y:0,opacity:1}).frames
 assert.deepEqual(frames[0],{transform:'translateY(4px) scale(0.98)',opacity:.6});assert.deepEqual(frames.at(-1),{transform:'translateY(0px) scale(1)',opacity:1})
})

test('GPU preparation cannot indefinitely block a menu or fail it on a lost window',async()=>{
 const {boundedPaintReady}=await import('../src/dialog-animation.ts')
 await boundedPaintReady(()=>Promise.resolve())
 await boundedPaintReady(()=>Promise.reject(Error('window closed')))
 await boundedPaintReady(()=>{throw Error('destroyed')})
 let late;await boundedPaintReady(()=>new Promise(resolve=>late=resolve),1);late()
})

test('closing while GPU preparation is pending prevents a late opening animation',async()=>{
 const {animateNativeDialog}=await import('../src/dialog-animation.ts')
 const saved=Object.fromEntries(['getComputedStyle','requestAnimationFrame','cancelAnimationFrame'].map(key=>[key,global[key]])),frames=new Map();let sequence=0,ready
 global.getComputedStyle=()=>({opacity:'0'});global.requestAnimationFrame=fn=>{frames.set(++sequence,fn);return sequence};global.cancelAnimationFrame=id=>frames.delete(id)
 try{
  let shadeStarted=0,shadeCancelled=0
  const scrim={style:{opacity:'0'},animate(){shadeStarted++;return{finished:new Promise(()=>{}),cancel(){shadeCancelled++}}}}
  const node={style:{opacity:'0'},dataset:{},animate(){assert.fail('a cancelled prepare must not animate')}},control=animateNativeDialog(node,scrim, {present:true,reduced:false,retained:true,instantHide:false,prepare:true,paintReady:()=>new Promise(resolve=>ready=resolve)})
  assert.equal(shadeStarted,1,'the real backdrop starts painting before panel preparation')
  frames.get(1)();frames.get(2)();control.cancel();await control.finished;ready();await Promise.resolve();await Promise.resolve()
  assert.equal(shadeCancelled,1)
 }finally{for(const [key,value]of Object.entries(saved)){if(value===undefined)delete global[key];else global[key]=value}}
})
