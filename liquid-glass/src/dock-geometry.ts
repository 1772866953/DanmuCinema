export type Matrix=[number,number,number,number,number,number]
export const translate=(x:number,y:number):Matrix=>[1,0,0,1,x,y]
export function multiply(a:Matrix,b:Matrix):Matrix{return[a[0]*b[0]+a[2]*b[1],a[1]*b[0]+a[3]*b[1],a[0]*b[2]+a[2]*b[3],a[1]*b[2]+a[3]*b[3],a[0]*b[4]+a[2]*b[5]+a[4],a[1]*b[4]+a[3]*b[5]+a[5]]}
export function inverse(a:Matrix):Matrix{const det=a[0]*a[3]-a[1]*a[2];return[a[3]/det,-a[1]/det,-a[2]/det,a[0]/det,(a[2]*a[5]-a[3]*a[4])/det,(a[1]*a[4]-a[0]*a[5])/det]}
export const cssMatrix=(a:Matrix)=>`matrix(${a.join(',')})`
export const centered=(a:Matrix,x:number,y:number)=>multiply(multiply(translate(x,y),a),translate(-x,-y))
// Reference geometry: a 10 dp rim and 8 dp inward bend. Dispersion resamples
// the actual scene at different wavelengths; it never paints a coloured rim.
export const dockPrism={edgeWidth:10,bend:8,mapAmplitude:104,padding:12}
export const glassWavelengths=[1.12,1,.88] as const
/** GlassBackdrop.kt records and zooms the whole source before deforming it. */
export function glassSourceMatrix(width:number,height:number,left:number,top:number,zoom:number):Matrix{
 return multiply(translate(dockPrism.padding+width/2,dockPrism.padding+height/2),multiply([zoom,0,0,zoom,0,0],translate(-left-width/2,-top-height/2)))
}
export function glassEdgeMask(width:number,height:number,feather:number){
 const edge=Math.min(dockPrism.edgeWidth,width/3,height/3),radius=Math.min(width,height)/2
 // One softly antialiased vector stroke replaces two hard, nested clip paths.
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><defs><filter id="a" x="-10%" y="-20%" width="120%" height="140%"><feGaussianBlur stdDeviation="${feather}"/></filter></defs><rect x="${edge/2}" y="${edge/2}" width="${width-edge}" height="${height-edge}" rx="${radius-edge/2}" fill="none" stroke="white" stroke-width="${edge}" filter="url(#a)"/></svg>`
 return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`
}
export function capsulePath(x:number,y:number,width:number,height:number){
 const w=Math.max(.01,width),h=Math.max(.01,height),r=Math.min(w,h)/2
 return `M${x+r} ${y}H${x+w-r}A${r} ${r} 0 0 1 ${x+w} ${y+r}V${y+h-r}A${r} ${r} 0 0 1 ${x+w-r} ${y+h}H${x+r}A${r} ${r} 0 0 1 ${x} ${y+h-r}V${y+r}A${r} ${r} 0 0 1 ${x+r} ${y}Z`
}
export function rimNormal(px:number,py:number,width:number,height:number,cornerRadius=Math.min(width,height)/2){
 const radius=Math.max(0,Math.min(cornerRadius,width/2,height/2)),qx=Math.abs(px-width/2)-(width/2-radius),qy=Math.abs(py-height/2)-(height/2-radius)
 const ox=Math.max(qx,0),oy=Math.max(qy,0),inside=-(Math.min(Math.max(qx,qy),0)+Math.hypot(ox,oy)-radius)
 let nx=ox*Math.sign(px-width/2),ny=oy*Math.sign(py-height/2);const n=Math.hypot(nx,ny)
 if(n){nx/=n;ny/=n}else if(qx>qy)nx=Math.sign(px-width/2);else ny=Math.sign(py-height/2)
 return{nx,ny,strength:Math.max(0,1-Math.max(0,inside)/dockPrism.edgeWidth)**2}
}
