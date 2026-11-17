import {rimNormal,dockPrism} from './dock-geometry'
self.onmessage=async({data:{key,width,height,radius}}:MessageEvent<{key:string;width:number;height:number;radius:number}>)=>{
 try{
  const {padding,mapAmplitude}=dockPrism,scale=height>150?Math.min(1,Math.sqrt(240000/((width+2*padding)*(height+2*padding)))):Math.min(3,2400/(width+2*padding))
  const w=Math.ceil((width+2*padding)*scale),h=Math.ceil((height+2*padding)*scale),canvas=new OffscreenCanvas(w,h),ctx=canvas.getContext('2d',{willReadFrequently:true})!,pixels=ctx.createImageData(w,h)
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
   const {nx,ny,strength}=rimNormal((x+.5)/scale-padding,(y+.5)/scale-padding,width,height,radius),i=(y*w+x)*4
   pixels.data[i]=Math.round(127.5+nx*mapAmplitude*strength);pixels.data[i+1]=Math.round(127.5+ny*mapAmplitude*strength);pixels.data[i+2]=128;pixels.data[i+3]=255
  }
  ctx.putImageData(pixels,0,0);const blob=await canvas.convertToBlob({type:'image/png'}),url=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=reject;reader.readAsDataURL(blob)})
  self.postMessage({key,url})
 }catch(error){self.postMessage({key,error:String(error)})}
}
