const test=require('node:test'),assert=require('node:assert/strict'),createReady=require('../electron/window-ready.cjs')
function fixture(){let finish,captures=0,destroyed=false;const window={isDestroyed:()=>destroyed,webContents:{capturePage:async(...args)=>{captures++;assert.deepEqual(args,[undefined,{stayHidden:true,stayAwake:true}]);await new Promise(r=>finish=r)}}};return{window,finish:()=>finish(),close:()=>destroyed=true,count:()=>captures}}
test('window stays unpresented until its GPU frame completes; repeated ready requests share that frame',async()=>{
 const f=fixture(),presented=[],ready=createReady({isCurrent:w=>w===f.window,onReady:w=>presented.push(w)}),a=ready(f.window),b=ready(f.window)
 assert.equal(f.count(),1);assert.equal(presented.length,0);f.finish();await Promise.all([a,b]);assert.deepEqual(presented,[f.window])
})
test('a late GPU completion cannot reveal a destroyed or replacement window',async()=>{
 for(const change of ['destroy','replace']){const f=fixture(),presented=[];let current=f.window;const ready=createReady({isCurrent:w=>w===current,onReady:w=>presented.push(w)}),pending=ready(f.window)
  if(change==='destroy')f.close();else current={};f.finish();await pending;assert.equal(presented.length,0)
 }
})
test('original navigation readiness waits for page/form initialization before capturing the final scene',async()=>{
 const f=fixture(),presented=[];let finishUi
 const ready=createReady({isCurrent:w=>w===f.window,beforeCapture:()=>new Promise(resolve=>finishUi=resolve),onReady:w=>presented.push(w)}),pending=ready(f.window)
 assert.equal(f.count(),0);finishUi();await Promise.resolve();assert.equal(f.count(),1);assert.equal(presented.length,0);f.finish();await pending;assert.deepEqual(presented,[f.window])
})
test('a window closed while page initialization is pending is never captured or presented',async()=>{
 const f=fixture();let finishUi
 const ready=createReady({isCurrent:()=>true,beforeCapture:()=>new Promise(resolve=>finishUi=resolve),onReady:()=>assert.fail('closed window presented')}),pending=ready(f.window)
 f.close();finishUi();await pending;assert.equal(f.count(),0)
})
