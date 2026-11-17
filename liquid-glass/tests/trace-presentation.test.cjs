const {test}=require('node:test'),assert=require('node:assert/strict'),{presentedFrames,presentationSummary}=require('../scripts/trace-presentation.cjs')
const event=(name,ph,ts,args={},id='a',pid=7)=>({name,ph,ts,args,pid,tid:1,id2:{local:id}})
const root=(start,end,id='a',pid=7)=>[event('FrameSequenceTrackerV3','b',start,{name:'CompositorAnimation'},id,pid),event('FrameSequenceTrackerV3','e',end,{},id,pid)]
const frame=(start,end,expected,drops=0,id='a',pid=7)=>[event('Frame ','b',start,{data:{values:{expected,dropped_v3:drops,dropped_v4:drops}}},id,pid),event('Frame ','e',end,{},id,pid)]
test('presentation deltas retain real drops but do not infer drops from a no-update interval',()=>{
 const data=presentedFrames([...root(0,40),...frame(0,5,1),...frame(5,25,2),...frame(25,40,5,2)])
 assert.equal(presentationSummary(data,{pid:7,start:0,end:24}).zeroDropped,true)
 const result=presentationSummary(data,{pid:7,start:25,end:40});assert.equal(result.tracks[0].droppedV4,2);assert.equal(result.zeroDropped,false)
})
test('reused tracker counters are differenced across sequences and reset only when counters restart',()=>{
 const data=presentedFrames([...root(0,10),...frame(0,10,2,1),...root(20,30),...frame(20,30,4,1),...root(40,50),...frame(40,50,1)])
 assert.equal(presentationSummary(data,{pid:7,start:20,end:30}).zeroDropped,true);assert.equal(data[1].expectedDelta,2);assert.equal(data[2].expectedDelta,1)
})
test('a different renderer and missing feedback cannot certify the visible window',()=>{
 const data=presentedFrames([...root(0,10,'a',8),...frame(0,10,2,0,'a',8)])
 assert.equal(presentationSummary(data,{pid:7,start:0,end:10}).available,false);assert.equal(presentationSummary(data,{pid:7,start:0,end:10}).zeroDropped,false)
})
test('an active canvas tracker retains completed feedback without inventing an unfinished frame',()=>{
 const begin=event('FrameSequenceTrackerV3','b',0,{name:'CanvasAnimation'})
 const data=presentedFrames([begin,...frame(0,5,1),...frame(5,10,3,1),event('Frame ','b',10,{data:{values:{expected:4,dropped_v3:1,dropped_v4:1}}})])
 const result=presentationSummary(data,{pid:7,start:0,end:20,names:['CanvasAnimation']})
 assert.equal(result.available,true);assert.equal(result.tracks[0].frames,2);assert.equal(result.tracks[0].expected,3);assert.equal(result.tracks[0].droppedV3,1);assert.equal(result.zeroDropped,false)
 assert.equal(presentationSummary(presentedFrames([begin]),{pid:7,start:0,end:20,names:['CanvasAnimation']}).available,false)
})
