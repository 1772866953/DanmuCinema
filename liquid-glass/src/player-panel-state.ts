type State=Record<string,any>
// The desktop settings form does not display playback position, pause, speed
// or volume. Their telemetry must not rerender all sliders during an animation.
export function playerPanelSignature(state:State){
 const settings={...state.settings,volume:undefined,speed:undefined}
 return JSON.stringify([settings,...['active','starting','title','error','notice','danmu','audio-delay','sub-delay'].map(key=>state[key])])
}
export function playerDiagnosticSignature(state:State){return JSON.stringify([state['hwdec-current'],state['frame-drop-count'],state.video?.w,state.video?.h,state.video?.pixelformat,state.video?.primaries,state.video?.gamma,state.target?.primaries,state.target?.gamma])}
