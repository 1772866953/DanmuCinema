import {rimNormal,dockPrism} from './dock-geometry'
type Request={owner:string;sequence:number;key:string;width:number;height:number;radius:number}
const channel=globalThis as unknown as {postMessage(value:unknown):void;onmessage:((event:MessageEvent<Request|{cancel:string}>)=>void)|null}
const queued=new Map<string,Request>();let busy=false
async function pump(){
 if(busy)return;busy=true
 try{while(queued.size){
  const request=queued.values().next().value!;queued.delete(request.owner)
  const {width,height,radius}=request,{padding,mapAmplitude}=dockPrism,scale=Math.min(3,2400/(width+2*padding)),w=Math.ceil((width+2*padding)*scale),h=Math.ceil((height+2*padding)*scale)
  const canvas=new OffscreenCanvas(w,h),ctx=canvas.getContext('2d',{willReadFrequently:true})!,pixels=ctx.createImageData(w,h),stepX=(width+2*padding)/w,stepY=(height+2*padding)/h
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
   const {nx,ny,strength}=rimNormal((x+.5)*stepX-padding,(y+.5)*stepY-padding,width,height,radius),i=(y*w+x)*4
   pixels.data[i]=Math.round(128+nx*mapAmplitude*strength);pixels.data[i+1]=Math.round(128+ny*mapAmplitude*strength);pixels.data[i+2]=128;pixels.data[i+3]=255
  }
  ctx.putImageData(pixels,0,0);const blob=await canvas.convertToBlob({type:'image/png'})
  const url=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=reject;reader.readAsDataURL(blob)})
  channel.postMessage({...request,url})
 }}finally{busy=false}
}
channel.onmessage=event=>{if('cancel'in event.data){queued.delete(event.data.cancel);return}queued.set(event.data.owner,event.data);void pump()}
