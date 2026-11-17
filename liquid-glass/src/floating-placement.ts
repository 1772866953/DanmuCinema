export function fitFloatingMenu(x:number,y:number,width:number,height:number,viewportWidth:number,viewportHeight:number,margin=12){
 const availableWidth=Math.max(1,viewportWidth-2*margin),availableHeight=Math.max(1,viewportHeight-2*margin)
 return{left:Math.max(margin,Math.min(x,viewportWidth-Math.min(width,availableWidth)-margin)),top:Math.max(margin,Math.min(y,viewportHeight-Math.min(height,availableHeight)-margin)),maxWidth:availableWidth,maxHeight:availableHeight}
}
