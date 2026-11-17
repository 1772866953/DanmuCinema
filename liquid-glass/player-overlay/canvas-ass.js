import JASSUB from 'jassub'
// Canvas-only rendering has no video element to supply an aspect ratio.
// Explicitly size the ASS surface instead of letting the video resize path
// overwrite it with a zero/NaN size after the owned window changes bounds.
export class CanvasASS extends JASSUB{
 constructor(options){super(options);this.element=options.canvas;this.surfaceWidth=0;this.surfaceHeight=0}
 async resize(repaint=false,width=0,height=0){
  await this.ready
  width=Math.max(1,Math.round(width||this.element.clientWidth*devicePixelRatio));height=Math.max(1,Math.round(height||this.element.clientHeight*devicePixelRatio))
  if(width!==this.surfaceWidth||height!==this.surfaceHeight){await this.renderer._resizeCanvas(width,height,0,0);this.surfaceWidth=width;this.surfaceHeight=height}
  if(repaint&&this.demand)await this.renderer._draw(this.demand.mediaTime+this.timeOffset,true)
 }
 async manualRender(data,repaint=false){this.demand=data;await this.resize(false,data.width,data.height);await this.renderer._draw(data.mediaTime+this.timeOffset,repaint)}
}
