export {}
type Request={id:number;bitmap:ImageBitmap;width:number;height:number}
const channel=globalThis as unknown as {postMessage(value:unknown):void;onmessage:((event:MessageEvent<Request>)=>void)|null}
channel.onmessage=async({data:{id,bitmap,width,height}})=>{
 try{
 const canvas=new OffscreenCanvas(width,height),ctx=canvas.getContext('2d',{willReadFrequently:true})!
  ctx.drawImage(bitmap,0,0);bitmap.close()
  const blob=await canvas.convertToBlob({type:'image/png'})
  const url=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=reject;reader.readAsDataURL(blob)})
  channel.postMessage({id,url})
 }catch(error){bitmap.close();channel.postMessage({id,error:String(error)})}
}
