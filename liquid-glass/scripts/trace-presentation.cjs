'use strict'
// Chromium FrameSequenceMetrics publishes cumulative expected/drop counts for
// each presented frame. These account for NoUpdateDesired, unlike raw spacing
// between SwapBuffers calls. Keep both measurements; they answer different
// questions. See cc/metrics/frame_sequence_metrics.cc TraceData::Advance.
const key=e=>`${e.pid}:${e.tid}:${e.id2?.local??e.id??''}`
function presentedFrames(events){
 const roots=[],rootStack=new Map(),frames=[],frameStack=new Map()
 const sorted=events.filter(e=>e.name==='FrameSequenceTrackerV3'||/^Frame *$/.test(e.name)).slice().sort((a,b)=>a.ts-b.ts||(a.name==='FrameSequenceTrackerV3'&&a.ph==='b'?-1:b.name==='FrameSequenceTrackerV3'&&b.ph==='b'?1:0))
 for(const event of sorted){
  const root=event.name==='FrameSequenceTrackerV3',stack=root?rootStack:frameStack,k=key(event)+(root?'':':'+event.name)
  if(event.ph==='b'){const open=stack.get(k)||[];open.push(event);stack.set(k,open)}
  else if(event.ph==='e'){
   const begin=stack.get(k)?.pop();if(!begin)continue
   if(root)roots.push({key:key(begin),pid:begin.pid,name:begin.args?.name,start:begin.ts,end:event.ts})
   else{const values=begin.args?.data?.values||event.args?.data?.values;if(values)frames.push({key:key(begin),pid:begin.pid,start:begin.ts,end:event.ts,...values})}
  }
 }
 // A canvas tracker can remain active when tracing stops. Its completed
 // feedback frames are still valid; only its unfinished final frame is absent.
 for(const open of rootStack.values())for(const begin of open)roots.push({key:key(begin),pid:begin.pid,name:begin.args?.name,start:begin.ts,end:Infinity})
 const byKey=new Map();for(const root of roots){const list=byKey.get(root.key)||[];list.push(root);byKey.set(root.key,list)}
 const previous=new Map(),result=[]
 for(const frame of frames.sort((a,b)=>a.end-b.end)){
  const root=byKey.get(frame.key)?.find(root=>root.start<=frame.start&&root.end>=frame.end);if(!root)continue
  const k=frame.key+':'+root.name,old=previous.get(k),continued=old&&frame.expected>=old.expected&&frame.dropped_v3>=old.dropped_v3&&frame.dropped_v4>=old.dropped_v4
  result.push({...frame,name:root.name,expectedDelta:frame.expected-(continued?old.expected:0),droppedV3:frame.dropped_v3-(continued?old.dropped_v3:0),droppedV4:frame.dropped_v4-(continued?old.dropped_v4:0)})
  previous.set(k,frame)
 }
 return result
}
function presentationSummary(frames,{pid,start,end,names=['CompositorAnimation','NativePropertyAnimation']}){
 const selected=frames.filter(frame=>frame.pid===pid&&names.includes(frame.name)&&frame.end>=start&&frame.start<end)
 const tracks=[...new Set(selected.map(frame=>frame.name))].map(name=>{const rows=selected.filter(frame=>frame.name===name);return{name,frames:rows.length,expected:rows.reduce((n,frame)=>n+frame.expectedDelta,0),droppedV3:rows.reduce((n,frame)=>n+frame.droppedV3,0),droppedV4:rows.reduce((n,frame)=>n+frame.droppedV4,0)}})
 return{available:tracks.some(track=>track.expected>0),tracks,zeroDropped:tracks.length>0&&tracks.every(track=>track.expected>0&&track.droppedV3===0&&track.droppedV4===0)}
}
function marker(events,name){return events.find(e=>e.name===name&&e.cat?.includes('user_timing'))||events.find(e=>e.name==='TimeStamp'&&e.args?.data?.message===name)}
module.exports={presentedFrames,presentationSummary,marker}
