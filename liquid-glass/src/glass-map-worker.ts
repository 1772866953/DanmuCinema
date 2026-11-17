// Keep the original control optics while moving pixel generation and PNG
// encoding away from the input and animation thread.
self.onmessage=async(event:MessageEvent<{key:string;width:number;height:number;radius:number}>)=>{
 const {key,width,height,radius}=event.data
 try{
  const ratio=Math.min(1,720/width,90/height),w=Math.max(1,Math.round(width*ratio)),h=Math.max(1,Math.round(height*ratio)),r=Math.min(radius,width/2,height/2)*ratio
  const canvas=new OffscreenCanvas(w,h),ctx=canvas.getContext('2d',{willReadFrequently:true})!,pixels=ctx.createImageData(w,h)
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
   const qx=Math.abs(x+.5-w/2)-(w/2-r),qy=Math.abs(y+.5-h/2)-(h/2-r),ox=Math.max(qx,0),oy=Math.max(qy,0),distance=-(Math.min(Math.max(qx,qy),0)+Math.hypot(ox,oy)-r)
   let nx=ox*Math.sign(x+.5-w/2),ny=oy*Math.sign(y+.5-h/2);const length=Math.hypot(nx,ny)
   if(length){nx/=length;ny/=length}else if(qx>qy)nx=Math.sign(x+.5-w/2);else ny=Math.sign(y+.5-h/2)
   const strength=Math.max(0,1-Math.max(0,distance)/(7*ratio))**2,at=(y*w+x)*4
   pixels.data[at]=128+nx*110*strength;pixels.data[at+1]=128+ny*110*strength;pixels.data[at+2]=128;pixels.data[at+3]=255
  }
  ctx.putImageData(pixels,0,0)
  const blob=await canvas.convertToBlob({type:'image/png'})
  const url=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=reject;reader.readAsDataURL(blob)})
  self.postMessage({key,url})
 }catch(error){self.postMessage({key,error:String(error)})}
}
