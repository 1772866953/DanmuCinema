import {memo,useCallback,useEffect,useRef,useState,type ComponentProps} from 'react'
import {RefreshCw,Activity} from 'lucide-react'
import {GlassBackdropContext,GlassButton as Button} from './glass'
import {Continuous,Stepped} from './numeric'
import {SelectControl} from './controls'
import {CheckBox,Field,Input,Modal} from './ui'
import type {Data} from './api'
import './player.css'
import {requestGlassMap} from './glass-maps'
import {afterPaint,finishStartupWarmup} from './startup-warmup'
import {playerPanelSignature,playerDiagnosticSignature} from './player-panel-state'
let cachedPlayerState:Data={active:false,settings:{},tracks:[]}
let playerCacheRevision=0
export function cachePlayerState(next:Data){cachedPlayerState=next;playerCacheRevision++}
// Warm the fixed slider lens and saved settings while the foreground is idle;
// opening the dialog should not start its first worker or lay out defaults
// only to replace every value with the persisted settings a moment later.
export function warmPlayerPanel(){
 const revision=playerCacheRevision
 return Promise.all([window.cinema.player('state').then(next=>{if(revision===playerCacheRevision)cachePlayerState(next)}).catch(()=>{}),requestGlassMap(25.6,25.6,12.8).catch(()=>{})])
}
// Prime the expensive form during the existing foreground startup warmup.
// It never owns focus or input, performs no writes and is discarded before
// navigation becomes interactive. No hidden form remains in the tray.
export function PlayerPanelWarmup(){
 const [phase,setPhase]=useState('waiting'),live=useRef(true),finishing=useRef(false)
 useEffect(()=>{live.current=true;void Promise.all([document.fonts.ready,warmPlayerPanel()]).then(afterPaint).then(()=>{if(live.current)setPhase('active')});return()=>{live.current=false}},[])
 useEffect(()=>{let current=true;if(phase==='done')void afterPaint().then(()=>{if(current)finishStartupWarmup('player-form')});return()=>{current=false}},[phase])
 const finish=useCallback(()=>{if(finishing.current)return;finishing.current=true;void afterPaint().then(()=>window.cinema.window('prime')).catch(()=>{}).finally(()=>{if(live.current)setPhase('done')})},[])
 return phase==='active'?<Modal title="本地播放与弹幕" wide warmup onClose={()=>{}} onEntered={finish}><PlayerPanel/></Modal>:null
}
// Keep one settings form ready while its main renderer exists. It is parked
// without input or animation after close; closing to tray destroys the renderer
// and this form together. Telemetry filtering prevents hidden control rebuilds.
export function RetainedPlayerDialog({open,top,order,onClose}:{open:boolean;top:boolean;order:number;onClose():void}){
 const [phase,setPhase]=useState('waiting'),live=useRef(true),finishing=useRef(false),used=useRef(false)
 if(open)used.current=true
 useEffect(()=>{live.current=true;void Promise.all([document.fonts.ready,warmPlayerPanel()]).then(afterPaint).then(()=>{if(live.current)setPhase('warming')});return()=>{live.current=false}},[])
 useEffect(()=>{let current=true;if(phase==='parked')void afterPaint().then(()=>{if(current)finishStartupWarmup('player-form')});return()=>{current=false}},[phase])
 const finish=useCallback(()=>{if(finishing.current)return;finishing.current=true;void afterPaint().then(()=>window.cinema.window('prime')).catch(()=>{}).finally(()=>{if(live.current)setPhase('parked')})},[])
 if(phase==='waiting')return null
 return <Modal title="本地播放与弹幕" wide retained visible={open||phase==='warming'} warmup={!open&&phase==='warming'} instantHide={!open&&phase==='parked'&&!used.current} onEntered={phase==='warming'?finish:undefined} onClose={onClose} top={top} order={order}><PlayerPanel/></Modal>
}
function Select({hint,...props}:ComponentProps<typeof SelectControl>&{hint?:string}){return <div className="field" title={hint}><span>{props.label}</span><SelectControl {...props}/></div>}
function SubtitleFont({value,onCommit}:{value:string;onCommit(value:string):void}){
 const [draft,setDraft]=useState(value);useEffect(()=>setDraft(value),[value])
 const commit=()=>{if(draft.trim()&&draft.trim()!==value)onCommit(draft.trim());else setDraft(value)}
 return <Field label="字幕字体"><Input aria-label="字幕字体" value={draft} placeholder="系统已安装的字体名称" onChange={e=>setDraft(e.target.value)} onBlur={commit} onKeyDown={e=>{if(e.key==='Enter')commit()}}/></Field>
}
function SubtitleColor({label,value,onCommit}:{label:string;value:string;onCommit(value:string):void}){
 const [draft,setDraft]=useState(value);useEffect(()=>setDraft(value),[value])
 const commit=()=>{if(/^#[\da-f]{6}$/i.test(draft)){if(draft.toUpperCase()!==value)onCommit(draft.toUpperCase())}else setDraft(value)}
 return <Field label={label}><div className="subtitle-color"><input type="color" aria-label={`${label}选色`} value={value} onChange={e=>onCommit(e.target.value)}/><Input aria-label={label} value={draft} maxLength={7} onChange={e=>setDraft(e.target.value)} onBlur={commit} onKeyDown={e=>{if(e.key==='Enter')commit()}}/></div></Field>
}
export const PlayerPanel=memo(function PlayerPanel(){
 const [autoMatch,setAutoMatch]=useState(true),[autoMatchBusy,setAutoMatchBusy]=useState(false)
 useEffect(()=>{const update=(v:Data)=>setAutoMatch(v.settings?.AutoMatchDanmu!==false);window.cinema.call('state').then(update).catch(()=>{});return window.cinema.onState(update)},[])
 const toggleAutoMatch=async(enabled:boolean)=>{setAutoMatchBusy(true);try{await window.cinema.call('autoMatch',{enabled});setAutoMatch(enabled)}catch(e){setError((e as Error).message)}finally{setAutoMatchBusy(false)}}
 const [state,setState]=useState<Data>(()=>cachedPlayerState),[error,setError]=useState(''),[offset,setOffset]=useState(()=>String(cachedPlayerState.settings?.offset||0))
 const signature=useRef(playerPanelSignature(cachedPlayerState))
 const pending=useRef(new Map<string,{value:any;token:object}>()),saveChain=useRef(Promise.resolve())
 const receive=useCallback((next:Data,cache=true)=>{if(cache)cachePlayerState(next);const merged:Data={...next,settings:{...next.settings}};for(const [key,{value}] of pending.current){if(key==='audioDelay')merged['audio-delay']=value;else if(key==='subtitleDelay')merged['sub-delay']=value;else merged.settings[key]=value}const nextSignature=playerPanelSignature(merged);if(signature.current!==nextSignature){signature.current=nextSignature;setState(merged)}},[])
 const action=async(name:string,value?:any)=>{try{setError('');const next=await window.cinema.player(name,value);if(next)receive(next)}catch(e){setError((e as Error).message)}}
 useEffect(()=>{action('state');return window.cinema.onPlayer(receive)},[receive])
 useEffect(()=>{setOffset(String(state.settings?.offset||0))},[state.settings?.offset])
 const s=state.settings||{},save=(value:Data)=>{
  const token={};for(const [key,v] of Object.entries(value))pending.current.set(key,{value:v,token})
  receive(state,false);setError('')
  const clear=()=>{for(const key of Object.keys(value))if(pending.current.get(key)?.token===token)pending.current.delete(key)}
  // Keep controls interactive. Serialize writes and overlay pending choices on
  // periodic player events so an older response cannot flash the whole panel.
  saveChain.current=saveChain.current.then(async()=>{try{const next=await window.cinema.player('settings',value);clear();if(next)receive(next)}catch(e){clear();setError((e as Error).message);try{receive(await window.cinema.player('state'))}catch{}}})
  return saveChain.current
 }
 const busy=!!state.starting
 return <div className="player-panel">
  <div className="player-status" role="status"><strong>{state.active?state.title:'本地播放器'}</strong><span>{state.error||state.danmu||'双击列表项目或封面播放。'}</span></div>
  {(error||state.notice)&&<p className={error?'error-text':'muted'} role="status">{error||state.notice}</p>}
  <section className="player-section"><h3>弹幕</h3><div className="player-auto-match"><CheckBox disabled={autoMatchBusy} checked={autoMatch} onChange={v=>void toggleAutoMatch(v)}>自动在线匹配弹幕</CheckBox><p className="muted">关闭后仍加载本地弹幕，可手动搜索下载。</p></div><div className="actions toolbar-wrap"><CheckBox disabled={busy} checked={s.enabled!==false} onChange={v=>save({enabled:v})}>显示弹幕</CheckBox><CheckBox disabled={busy} checked={s.avoidSubtitles!==false} onChange={v=>save({avoidSubtitles:v})}>避让底部字幕</CheckBox><CheckBox disabled={busy} checked={s.simplified!==false} onChange={simplified=>save({simplified})}>繁体转简体</CheckBox><CheckBox disabled={busy} checked={!!s.white} onChange={v=>save({white:v})}>统一白色</CheckBox><Button disabled={!state.active||busy} onClick={()=>action('reloadDanmu')}><RefreshCw size={14}/>重新读取弹幕</Button></div>
  <div className="player-grid">
   <Continuous label="显示区域" hint="占画面高度的比例；0% 不显示弹幕。" value={Number(((s.region??.5)*100).toFixed(1))} resetValue={50} min={0} max={100} step={1} unit="%" disabled={busy} onCommit={v=>save({region:v/100})}/>
   <Continuous label="弹幕字号" value={s.fontSize??28} resetValue={28} min={12} max={96} step={.1} disabled={busy} onCommit={fontSize=>save({fontSize})}/>
   <Continuous label="透明度" value={Number(((s.opacity??.85)*100).toFixed(1))} resetValue={85} min={10} max={100} step={.1} unit="%" disabled={busy} onCommit={v=>save({opacity:v/100})}/>
   <Continuous resetValue={8} label="滚动时长" hint="一条弹幕横穿画面的时间，数值越大移动越慢。" value={s.duration??8} min={8} max={32} disabled={busy} onCommit={duration=>save({duration})}/>
   <Continuous resetValue={400} label="弹幕字重" value={s.fontWeight??400} min={100} max={900} step={10} disabled={busy} onCommit={fontWeight=>save({fontWeight})}/>
   <Stepped label="弹幕帧率" value={s.danmuFps??60} resetValue={60} disabled={busy} options={[{value:60,label:'60 帧/秒'},{value:120,label:'120 帧/秒'},{value:0,label:'跟随显示器刷新率'}]} onCommit={danmuFps=>save({danmuFps})}/>
   <Stepped label="同屏上限" value={s.density??80} resetValue={80} disabled={busy} options={[20,40,60,80,120,160].map(v=>({value:v,label:`${v} 条`}))} format={v=>`${v} 条`} onCommit={density=>save({density})}/>
   <Field label="时间偏移 / 秒（正值延后）"><div className="actions player-offset"><Input aria-label="弹幕时间偏移" type="number" min="-3600" max="3600" step=".5" value={offset} onChange={e=>setOffset(e.target.value)}/><Button disabled={busy||!Number.isFinite(Number(offset))} onClick={()=>save({offset:Number(offset)})}>应用</Button><Button disabled={busy} onClick={()=>save({offset:0})}>重置</Button></div></Field>
  </div>
  <p className="muted">弹幕帧率不超过屏幕刷新率。</p>
  <div className="actions toolbar-wrap">{[['scroll','滚动'],['top','顶部固定'],['bottom','底部固定']].map(([key,label])=><CheckBox key={key} disabled={busy} checked={s[key]!==false} onChange={v=>save({[key]:v})}>{label}</CheckBox>)}</div></section>
  <section className="player-section"><h3>音频与字幕同步</h3><div className="player-grid"><Continuous resetValue={0} label="音频偏移 / 秒" value={state['audio-delay']||0} min={-30} max={30} step={.05} disabled={!state.active||busy} onCommit={audioDelay=>save({audioDelay})}/><Continuous resetValue={0} label="字幕偏移 / 秒" value={state['sub-delay']||0} min={-30} max={30} step={.05} disabled={!state.active||busy} onCommit={subtitleDelay=>save({subtitleDelay})}/></div><p className="muted">正值延后，负值提前；按影片保存。</p></section><section className="player-section"><h3>字幕样式</h3><CheckBox checked={s.subtitleOriginal!==false} onChange={subtitleOriginal=>save({subtitleOriginal})}>保留字幕原样式</CheckBox>{s.subtitleOriginal===false&&<div className="player-grid subtitle-settings">
   <SubtitleFont value={s.subtitleFont||'sans-serif'} onCommit={subtitleFont=>save({subtitleFont})}/>
   <Continuous label="字幕字号" value={s.subtitleSize??38} min={12} max={96} step={.5} resetValue={38} onCommit={subtitleSize=>save({subtitleSize})}/>
   <Stepped label="字幕字重" value={s.subtitleWeight??400} resetValue={400} options={[{value:400,label:'常规'},{value:700,label:'粗体'}]} onCommit={subtitleWeight=>save({subtitleWeight})}/>
   <div className="player-checks"><CheckBox checked={!!s.subtitleItalic} onChange={subtitleItalic=>save({subtitleItalic})}>字幕斜体</CheckBox></div>
   <SubtitleColor label="字幕文字颜色" value={s.subtitleColor||'#FFFFFF'} onCommit={subtitleColor=>save({subtitleColor})}/>
   <Continuous label="字幕不透明度" value={Math.round((s.subtitleOpacity??1)*100)} min={0} max={100} step={1} resetValue={100} unit="%" onCommit={v=>save({subtitleOpacity:v/100})}/>
   <Continuous label="字幕描边宽度" value={s.subtitleOutline??1.65} min={0} max={8} step={.05} resetValue={1.65} onCommit={subtitleOutline=>save({subtitleOutline})}/>
   <SubtitleColor label="字幕描边颜色" value={s.subtitleOutlineColor||'#000000'} onCommit={subtitleOutlineColor=>save({subtitleOutlineColor})}/>
   <Continuous label="字幕阴影距离" value={s.subtitleShadow??0} min={0} max={10} step={.1} resetValue={0} onCommit={subtitleShadow=>save({subtitleShadow})}/>
   <SubtitleColor label="字幕阴影颜色" value={s.subtitleShadowColor||'#000000'} onCommit={subtitleShadowColor=>save({subtitleShadowColor})}/>
  </div>}{s.subtitleOriginal===false&&<p className="muted">图形字幕不支持修改字体。</p>}</section><section className="player-section"><h3>播放器界面</h3><Stepped label="长按倍速" value={s.holdSpeed??2} resetValue={2} options={[2,3,4,5,6].map(value=>({value,label:`${value} 倍`}))} onCommit={holdSpeed=>save({holdSpeed})}/><Stepped label="界面动效帧率" value={s.uiFps??0} resetValue={0} disabled={busy} hint="自动跟随屏幕刷新率；固定档位不超过屏幕刷新率。" options={[{value:0,label:'自动'},...[60,120,240].map(v=>({value:v,label:`${v} 帧/秒`}))]} onCommit={uiFps=>save({uiFps})}/><Continuous resetValue={16} label="播放器界面字号" value={s.uiFontSize||16} min={12} max={24} step={.5} disabled={busy} onCommit={uiFontSize=>save({uiFontSize})}/><Continuous resetValue={400} label="播放器界面字重" value={s.uiFontWeight||400} min={100} max={900} step={50} disabled={busy} onCommit={uiFontWeight=>save({uiFontWeight})}/></section><section className="player-section"><h3>解码与色彩</h3><Continuous label="播放预读预算" value={s.preloadBudget??25} min={0} max={100} step={1} resetValue={25} disabled={busy} unit="%" hint="调整内存预读缓存，不代表 GPU 占用率。" onCommit={preloadBudget=>save({preloadBudget})}/><div className="player-grid"><Select label="输出模式" value={s.colorMode||'auto'} onChange={v=>save({colorMode:v})} options={[{value:'auto',label:'自动 · 跟随系统和显示器'},{value:'sdr',label:'SDR 显示 · HDR 自动映射'}]}/><div className="player-checks"><CheckBox disabled={busy} checked={s.hardware!==false} onChange={v=>save({hardware:v})}>硬件解码（不支持时自动回退）</CheckBox><CheckBox disabled={busy} checked={!!s.icc} onChange={v=>save({icc:v})}>使用系统 ICC 配置</CheckBox><CheckBox disabled={busy} checked={!!s.spdif} onChange={v=>save({spdif:v})}>音频直通（需兼容功放）</CheckBox><CheckBox disabled={busy} checked={s.resume!==false} onChange={v=>save({resume:v})}>记忆播放位置</CheckBox></div></div><p className="muted">色彩模式与 ICC 在下次播放时生效。</p>
  {state.active&&<PlayerDiagnostics action={action}/>}</section>

 </div>
})

function PlayerDiagnostics({action}:{action(name:string):Promise<void>}){
 const [state,setState]=useState<Data>(()=>cachedPlayerState),signature=useRef(playerDiagnosticSignature(cachedPlayerState))
 useEffect(()=>window.cinema.onPlayer(next=>{const value=playerDiagnosticSignature(next);if(value!==signature.current){signature.current=value;setState(next)}}),[])
 return <div className="player-diagnostics"><span>解码：{state['hwdec-current']&&state['hwdec-current']!=='no'?state['hwdec-current']:'软件解码'}</span><span>输入：{state.video?.w||'—'} × {state.video?.h||'—'} · {state.video?.pixelformat||'—'} · {state.video?.primaries||'—'} / {state.video?.gamma||'—'}</span><span>输出：{state.target?.primaries||'—'} / {state.target?.gamma||'—'} · 丢帧 {state['frame-drop-count']||0}</span><Button onClick={()=>action('statistics')}><Activity size={14}/>播放诊断</Button></div>
}
