export type LiquidInput = {up(event:PointerEvent):void;cancel():void}
// Only controls currently under the pointer or being dragged need global
// cancellation. Mounting a page must not install three listeners per button.
export function createLiquidInput(getTarget:()=>EventTarget){
 const active=new Set<LiquidInput>();let target:EventTarget|null=null
 const up=(event:Event)=>{for(const input of [...active])input.up(event as PointerEvent)}
 const cancel=()=>{for(const input of [...active])input.cancel()}
 const add=(input:LiquidInput)=>{if(active.has(input))return;active.add(input);if(target)return;target=getTarget();target.addEventListener('pointerup',up);target.addEventListener('pointercancel',cancel);target.addEventListener('blur',cancel)}
 const remove=(input:LiquidInput)=>{active.delete(input);if(active.size||!target)return;target.removeEventListener('pointerup',up);target.removeEventListener('pointercancel',cancel);target.removeEventListener('blur',cancel);target=null}
 return{add,remove}
}
export const liquidInput=createLiquidInput(()=>window)
