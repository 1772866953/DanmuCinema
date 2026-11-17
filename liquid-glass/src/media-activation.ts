// Media rows dispatch on double click without a single-click timer.
export function useMediaActivation(_interval:number,_context:string){
 const double=(event:React.MouseEvent,action:()=>void)=>{
  if((event.target as HTMLElement).closest('button:not(.media-name-trigger):not(.poster-image-button),input,label,[role="checkbox"]'))return
  event.preventDefault();action()
 }
 return{double}
}
