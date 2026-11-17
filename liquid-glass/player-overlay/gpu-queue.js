// GL commands already preserve read/write order. Bound outstanding submissions
// with zero-timeout fences instead of blocking the worker with gl.finish().
export class GPUQueue{
 constructor(gl,limit=2){this.gl=gl;this.limit=limit;this.fences=[];this.stats={submitted:0,skipped:0,busy:0,peak:0,failures:0}}
 ready(){
  const gl=this.gl;if(gl.isContextLost()){this.reset();return false}
  // Read only the oldest fence when a slot is needed. Querying the most recent
  // submission at every frame adds an unnecessary renderer/GPU round trip.
  while(this.fences.length>=this.limit){const result=gl.clientWaitSync(this.fences[0],0,0);if(result===gl.TIMEOUT_EXPIRED)break;gl.deleteSync(this.fences.shift());if(result===gl.WAIT_FAILED)this.stats.failures++}
  if(this.fences.length>=this.limit){this.stats.busy++;return false}
  return true
 }
 submit(){const gl=this.gl;if(gl.isContextLost())return;const fence=gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE,0);if(!fence){this.stats.failures++;return}this.fences.push(fence);this.stats.submitted++;this.stats.peak=Math.max(this.stats.peak,this.fences.length);gl.flush()}
 discard(){this.stats.skipped++}
 snapshot(reset=false){const result={...this.stats,pending:this.fences.length};if(reset)this.stats={submitted:0,skipped:0,busy:0,peak:0,failures:0};return result}
 reset(){this.fences.forEach(fence=>this.gl.deleteSync(fence));this.fences=[]}
}
