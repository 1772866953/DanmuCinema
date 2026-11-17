// Compare decoded shader input with the original renderer algorithm, not PNG
// encoder bytes. Worker encoding must not alter the visible refraction field.
import {rimNormal,dockPrism} from '../src/dock-geometry.ts'
import {runtime} from './test-runtime.mjs'
export async function verifyPanelMap(page){
 await page.waitForFunction(()=>document.querySelector('.panel-optics feImage')?.getAttribute('href')?.startsWith('data:image/png'))
 const sample=await page.locator('.panel-optics').first().evaluate(node=>{const f=node.querySelector('filter');return {width:Number(f.getAttribute('width'))-24,height:Number(f.getAttribute('height'))-24,radius:parseFloat(getComputedStyle(node).borderTopLeftRadius),url:f.querySelector('feImage').getAttribute('href')}})
 const {width,height,radius}=sample,{padding,mapAmplitude}=dockPrism,scale=height>150?Math.min(1,Math.sqrt(240000/((width+2*padding)*(height+2*padding)))):Math.min(3,2400/(width+2*padding)),w=Math.ceil((width+2*padding)*scale),h=Math.ceil((height+2*padding)*scale),actual=await runtime('sharp')(Buffer.from(sample.url.split(',')[1],'base64')).ensureAlpha().raw().toBuffer({resolveWithObject:true})
 if(actual.info.width!==w||actual.info.height!==h)throw Error('Panel field dimensions changed')
 let differences=0
 for(let y=0;y<h;y++)for(let x=0;x<w;x++){const {nx,ny,strength}=rimNormal((x+.5)/scale-padding,(y+.5)/scale-padding,width,height,radius),i=(y*w+x)*4,expected=[Math.round(127.5+nx*mapAmplitude*strength),Math.round(127.5+ny*mapAmplitude*strength),128,255];for(let c=0;c<4;c++)if(actual.data[i+c]!==expected[c])differences++}
 if(differences)throw Error('Panel refraction changed: '+differences+' channel values')
 return {width,height,radius,differences}
}
export async function verifyGlassMaps(page){
 await page.waitForFunction(()=>document.querySelectorAll('.media-toolbar .glass-material feImage').length>=3)
 return page.evaluate(async()=>{
  const results=[]
  for(const button of [...document.querySelectorAll('.media-toolbar .glass-button')].filter(e=>e.querySelector('feImage')).slice(0,4)){
   const material=button.querySelector('.glass-material'),width=material.offsetWidth,height=material.offsetHeight,radius=Math.min(parseFloat(getComputedStyle(button).borderTopLeftRadius)||20,width/2,height/2)
   const ratio=Math.min(1,720/width,90/height),w=Math.max(1,Math.round(width*ratio)),h=Math.max(1,Math.round(height*ratio)),r=radius*ratio
   const image=new Image();image.src=button.querySelector('feImage').getAttribute('href');await image.decode()
   if(image.width!==w||image.height!==h)throw new Error('Shader map dimensions changed')
   const canvas=document.createElement('canvas');canvas.width=w;canvas.height=h;const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);const actual=ctx.getImageData(0,0,w,h).data,expected=new Uint8ClampedArray(w*h*4)
   for(let y=0;y<h;y++)for(let x=0;x<w;x++){
    const qx=Math.abs(x+.5-w/2)-(w/2-r),qy=Math.abs(y+.5-h/2)-(h/2-r),ox=Math.max(qx,0),oy=Math.max(qy,0),distance=-(Math.min(Math.max(qx,qy),0)+Math.hypot(ox,oy)-r)
    let nx=ox*Math.sign(x+.5-w/2),ny=oy*Math.sign(y+.5-h/2);const length=Math.hypot(nx,ny)
    if(length){nx/=length;ny/=length}else if(qx>qy)nx=Math.sign(x+.5-w/2);else ny=Math.sign(y+.5-h/2)
    const strength=Math.max(0,1-Math.max(0,distance)/(7*ratio))**2,at=(y*w+x)*4
    expected[at]=128+nx*110*strength;expected[at+1]=128+ny*110*strength;expected[at+2]=128;expected[at+3]=255
   }
   let differences=0;for(let i=0;i<actual.length;i++)if(actual[i]!==expected[i])differences++
   if(differences)throw new Error('Refraction map changed: '+differences+' channel values')
   results.push({width,height,radius,differences})
  }
  return results
 })
}
