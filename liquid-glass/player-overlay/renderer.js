// ASS overlays for the original mpv window. Video/subtitles stay in mpv.
import {CanvasASS} from './canvas-ass.js'
import {getUILayer} from './ui-layer-cache.js'
import clockWorkerUrl from './clock-worker.js?worker&url'
import wasmUrl from 'jassub/dist/wasm/jassub-worker.wasm?url'
import modernWasmUrl from 'jassub/dist/wasm/jassub-worker-modern.wasm?url'
import wasmWorkerUrl from 'jassub/dist/wasm/jassub-worker.js?url'
import modernWasmWorkerUrl from 'jassub/dist/wasm/jassub-worker-modern.js?url'
const initial=await window.overlayHost.bootstrap()
const canvas=document.getElementById('ass')
canvas.width=Math.round(innerWidth*devicePixelRatio);canvas.height=Math.round(innerHeight*devicePixelRatio)
const header=`[Script Info]
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 720
ScaledBorderAndShadow: yes
YCbCr Matrix: None
[V4+ Styles]
Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding
Style: Danmu,DanmuCinema UI,28,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1.1,0,7,0,0,0,1
[Events]
Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text
`
const stamp=s=>{const n=Math.max(0,Math.round(s*100));return `${Math.floor(n/360000)}:${String(Math.floor(n/6000)%60).padStart(2,'0')}:${String(Math.floor(n/100)%60).padStart(2,'0')}.${String(n%100).padStart(2,'0')}`}
const color=n=>[n&255,(n>>>8)&255,(n>>>16)&255].map(x=>x.toString(16).padStart(2,'0')).join('')
const font=new URL('../../../runtime/mpv/fonts/cinema-ui-400.ttf',location.href).href
const weight=value=>Math.max(100,Math.min(900,Math.round((Number(value)||400)/100)*100)),fontFor=value=>new URL(`../../../runtime/mpv/fonts/cinema-ui-${weight(value)}.ttf`,location.href).href
const danmuWeights=new Set([400,weight(initial.fontWeight)]),uiWeight=weight(initial.uiFontWeight)
const renderer=new CanvasASS({canvas,subContent:header,fonts:[...danmuWeights].map(fontFor),defaultFont:'DanmuCinema UI',queryFonts:false,workerUrl:clockWorkerUrl,wasmUrl,modernWasmUrl,wasmWorkerUrl,modernWasmWorkerUrl,maxRenderHeight:0,prescaleFactor:1})
// Preserve each native UI layer's original ASS resolution and font metrics.
const uiLayers=new Map(),uiCreating=new Map(),uiStats={updates:0,coalesced:0,costs:[],errors:[]};let recordUI=false
function makeUI(id){return getUILayer(uiLayers,uiCreating,id,async()=>{
 const node=document.createElement('canvas');node.dataset.uiLayer=id;node.style.zIndex='100';node.style.visibility='hidden';document.body.append(node)
 const icons=new URL('../../../runtime/mpv/fonts/modernz-icons.ttf',location.href).href
 const weights=new Set([400,uiWeight]),instance=new CanvasASS({canvas:node,subContent:header,fonts:[...[...weights].map(fontFor),icons],defaultFont:'DanmuCinema UI',queryFonts:false,workerUrl:clockWorkerUrl,wasmUrl,modernWasmUrl,wasmWorkerUrl,modernWasmWorkerUrl,maxRenderHeight:0,prescaleFactor:1})
 try{await instance.ready}catch(error){node.remove();throw error}
 return {node,instance,weights,active:false,pending:null,last:null}
})}
function uiSize(node){
 // Native client pixels are authoritative. Multiplying rounded CSS innerWidth
 // by fractional Windows DPI can produce a larger bitmap (2562 for 2560).
 const width=Math.round(geometry?.w||innerWidth*devicePixelRatio),height=Math.round(geometry?.h||innerHeight*devicePixelRatio)
 Object.assign(node.style,{width:`${width/devicePixelRatio}px`,height:`${height/devicePixelRatio}px`});return{width,height}
}
async function updateUI(s){
 if(s.id==='cinema:1')return
 const layer=uiLayers.get(s.id)||await makeUI(s.id)
 if(layer.active){if(layer.pending)uiStats.coalesced++;layer.pending=s;return}
 layer.active=true
 try{
  do{
   layer.pending=null;layer.last=s;const began=performance.now();layer.node.style.zIndex=String(s.z||0);layer.node.style.visibility=s.hidden?'hidden':'visible'
   if(!s.hidden){
    const head=header.replace('PlayResX: 1280',`PlayResX: ${s.res_x}`).replace('PlayResY: 720',`PlayResY: ${s.res_y}`)
    const track=head+s.data.split('\n').filter(Boolean).map(text=>`Dialogue: 0,0:00:00.00,9:00:00.00,Danmu,,0,0,0,,${text}`).join('\n')
    await layer.instance.renderer.setTrack(track)
    await layer.instance.manualRender({mediaTime:.5,...uiSize(layer.node),expectedDisplayTime:performance.now()})
   }
   uiStats.updates++;if(recordUI&&uiStats.costs.length<12000)uiStats.costs.push(performance.now()-began);s=layer.pending
  }while(s)
 }catch(e){if(uiStats.errors.length<64)uiStats.errors.push(String(e))}finally{layer.active=false}
}
async function primeUI(){await Promise.all(['modernz:1','modernz:2','cinema_input:1','select:1'].map(makeUI))}
let warmingUI
function warmUI(){return warmingUI??=(async()=>{
 const factor=(initial.uiFontSize||16)/16,logicalScale=devicePixelRatio*factor
 await Promise.all([...uiLayers.values()].map(async layer=>{
  const size=uiSize(layer.node),head=header.replace('PlayResX: 1280',`PlayResX: ${Math.round(size.width/logicalScale)}`).replace('PlayResY: 720',`PlayResY: ${Math.round(size.height/logicalScale)}`)
  const words='播放菜单弹幕字幕声音画面列表记录工具帮助设置退出全屏暂停继续音量速度时间偏移0123456789.:/%×'
  const seed=[12,14,16,18,22].map((n,i)=>`Dialogue: 0,0:00:00.00,9:00:00.00,Danmu,,0,0,0,,{\\an7\\pos(10,${10+i*32})\\fnDanmuCinema UI\\fs${n}\\b${uiWeight}\\bord0\\shad0}${words}`).join('\n')
  await layer.instance.renderer.setTrack(head+seed);await layer.instance.manualRender({mediaTime:.5,...size},true)
 }))
})()}
async function primeHints({width,height}){
 const layer=uiLayers.get('cinema_input:1'),node=layer?.node;if(!layer)return
 // Populate the intermittent hint's actual-size glyph masks before decoding.
 // Merely constructing its worker does not prepare its three rotating atlases.
 const seed='长按倍速 播放速度 音量 已暂停 继续播放 播放进度 0123456789×%:./',data=`{\\an7\\pos(20,20)\\fnDanmuCinema UI\\fs16\\b${uiWeight}}${seed}`
 node.style.visibility='hidden'
 const head=header.replace('PlayResX: 1280',`PlayResX: ${width}`).replace('PlayResY: 720',`PlayResY: ${height}`)
 await layer.instance.renderer.setTrack(head+`Dialogue: 0,0:00:00.00,9:00:00.00,Danmu,,0,0,0,,${data}`)
 for(let index=0;index<3;index++)await layer.instance.manualRender({mediaTime:.5,width:Math.round(innerWidth*devicePixelRatio),height:Math.round(innerHeight*devicePixelRatio)},true)
}
let anchor=0,speed=1,paused=false,requestedFps=0,displayHz=initial.displayHz||60,offset=0,drawWidth=canvas.width,drawHeight=canvas.height,geometry=null,workerRunning=false
function resize(){
 const d=geometry||{w:innerWidth*devicePixelRatio,h:innerHeight*devicePixelRatio,ml:0,mr:0,mt:0,mb:0}
 drawWidth=Math.max(1,d.w-(d.ml||0)-(d.mr||0));drawHeight=Math.max(1,d.h-(d.mt||0)-(d.mb||0))
 Object.assign(canvas.style,{left:`${(d.ml||0)/devicePixelRatio}px`,top:`${(d.mt||0)/devicePixelRatio}px`,width:`${drawWidth/devicePixelRatio}px`,height:`${drawHeight/devicePixelRatio}px`})
 if(workerRunning)void renderer.resize(false,drawWidth,drawHeight).then(()=>renderer.renderer.redrawClock())
}
window.addEventListener('resize',resize)
window.addEventListener('resize',()=>{for(const layer of uiLayers.values())if(layer.last&&!layer.last.hidden)void updateUI(layer.last)})
window.assOverlay={
 ready:renderer.ready,
 primeUI,
 warmUI,
 primeHints,
 updateUI,
 async uiStats(reset=false){const result={...uiStats,layers:[...uiLayers].map(([id,layer])=>({id,backing:[layer.node.width,layer.node.height],css:[layer.node.getBoundingClientRect().width,layer.node.getBoundingClientRect().height],dpr:devicePixelRatio,hidden:layer.node.style.visibility==='hidden'})),canvasCount:document.querySelectorAll('canvas[data-ui-layer]').length,gpu:await Promise.all([...uiLayers].map(async([id,layer])=>({id,...await layer.instance.renderer.renderQueueStats(reset)})))};if(reset){recordUI=true;uiStats.updates=0;uiStats.coalesced=0;uiStats.costs=[];uiStats.errors=[]}return result},
 async clear(){await renderer.ready;await renderer.renderer.stopClock();workerRunning=false;await renderer.renderer.setTrack(header);canvas.style.opacity='0'},
 async load(scene){
  if(!scene||scene.version!==1||!Array.isArray(scene.items))throw Error('Invalid danmu scene')
  const requestedWeight=weight(scene.fontWeight);if(!danmuWeights.has(requestedWeight)){await renderer.ready;await renderer.renderer.addFonts([fontFor(requestedWeight)]);danmuWeights.add(requestedWeight)}
  offset=scene.offset||0;canvas.style.opacity=String(scene.enabled?scene.opacity:0)
  let events=''
  for(const c of scene.items){const x=c.m==='scroll'?1280:c.m==='reverse'?-c.w:640,to=c.m==='scroll'?-c.w:c.m==='reverse'?1280:640
   const motion=c.m==='top'||c.m==='bottom'?`\\an8\\pos(${x},${c.y})`:`\\an7\\move(${x},${c.y},${to},${c.y},0,${Math.round(c.d*1000)})`
   events+=`Dialogue: 20,${stamp(c.t)},${stamp(c.t+c.d)},Danmu,,0,0,0,,{${motion}\\fs${scene.fontSize}\\b${scene.fontWeight||400}\\1c&H${color(c.c)}&}${c.text}\n`
  }
  await renderer.ready;await renderer.renderer.stopClock();await renderer.renderer.setTrack(header.replace('PlayResX: 1280',`PlayResX: ${scene.width}`).replace('PlayResY: 720',`PlayResY: ${scene.height}`)+events);await renderer.resize(false,drawWidth,drawHeight);await renderer.renderer.startClock({time:anchor,speed,paused,offset,fps:requestedFps,displayHz});workerRunning=true
 },
 async configure(settings){requestedFps=settings.danmuFps||0;offset=settings.offset||0;canvas.style.opacity=String(settings.enabled?settings.opacity:0);await renderer.renderer.configureClock({fps:requestedFps,offset});const requestedWeight=weight(settings.uiFontWeight);await Promise.all([...uiLayers.values()].map(async layer=>{if(!layer.weights.has(requestedWeight)){await layer.instance.renderer.addFonts([fontFor(requestedWeight)]);layer.weights.add(requestedWeight)}}));return true},
 geometry(value){geometry=value;resize();for(const layer of uiLayers.values())if(layer.last&&!layer.last.hidden)void updateUI(layer.last)},
 display(hz){if(Number.isFinite(hz)&&hz>=20&&hz<=1000){displayHz=hz;void renderer.renderer.configureClock({displayHz})}},
 clock(sample){anchor=sample.time;speed=sample.speed;paused=sample.paused;if(workerRunning)void renderer.renderer.sampleClock(sample)},
 stats(reset=false){return renderer.renderer.clockStats(reset)},
 diagnose(){return renderer.renderer.diagnoseClock()},
 async destroy(){await renderer.renderer.stopClock();await renderer.destroy();await Promise.all([...uiLayers.values()].map(layer=>layer.instance.destroy()))}
}
await renderer.ready
window.overlayHost?.onClock(sample=>window.assOverlay.clock(sample))
window.overlayHost?.onUI(sample=>updateUI(sample))
window.overlayHost?.onGeometry(value=>window.assOverlay.geometry(value))
window.overlayHost?.onDisplay(hz=>window.assOverlay.display(hz))
resize()
// Animation frames and GL submission are owned by clock-worker.js.

await primeUI();await warmUI();await window.assOverlay.configure(initial);window.overlayHost.ready()
