import {cachedGlassMap,glassMapKey,requestGlassMap} from './glass-maps'
import {afterPaint,finishStartupWarmup} from './startup-warmup'
import {liquidInput,type LiquidInput} from './liquid-input'
import { Children, createContext, forwardRef, useContext, useEffect, useLayoutEffect, useId, useImperativeHandle, useMemo, useRef, useState, type ButtonHTMLAttributes, type InputHTMLAttributes, type PointerEvent, type ReactNode } from 'react'
import { motion, useMotionTemplate, useSpring, useTransform, type MotionProps } from 'motion/react'

export const AppearanceContext = createContext({ reduced: false })
// An enclosing blurred surface already supplies the backdrop. Nested controls
// keep their fill/rim/motion but must not establish another changing sample root.
export const GlassBackdropContext = createContext(false)
export const liquidPhysics = { stiffness: 420, damping: 22, mass: .65 }
const sliderThumbSize = 25.6

export function GlassOpticsWarmup(){
 const [phase,setPhase]=useState('cold')
 useEffect(()=>{
  let live=true
  requestGlassMap(sliderThumbSize,sliderThumbSize,sliderThumbSize/2).then(()=>{if(live)setPhase('shown')}).catch(()=>{if(live)setPhase('done')})
  return()=>{live=false}
 },[])
 useEffect(()=>{let live=true;if(phase==='shown')void afterPaint().then(()=>{if(live)setPhase('done')});if(phase==='done')void afterPaint().then(()=>{if(live)finishStartupWarmup('slider-optics')});return()=>{live=false}},[phase])
 // A tiny, effectively invisible pane compiles the same blur/lens shaders at
 // idle. It is removed immediately afterwards and never runs an idle animation.
 return phase==='shown'?<div data-startup-warmup="slider-optics" aria-hidden="true" inert style={{position:'fixed',left:0,top:0,width:120,height:60,opacity:.001,pointerEvents:'none',backdropFilter:'blur(9px)',zIndex:1}}><GlassSlider value={50} min={0} max={100} tabIndex={-1}/></div>:null
}

type OpticalMode = 'refractive' | 'clear' | 'inherited'
export function useGlassOptics({ width, height, radius }: { width: number; height: number; radius: number }, mode: OpticalMode = 'refractive', strength=14) {
  const id = `lens-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const supported = useMemo(() => CSS.supports('backdrop-filter', 'blur(2px)') && !matchMedia('(prefers-reduced-transparency: reduce)').matches, [])
  const active=supported&&mode==='refractive'&&typeof SVGFEDisplacementMapElement!=='undefined'&&width>0&&height>0,key=glassMapKey(width,height,radius)
  const [lastMap,setMap]=useState(''),map=active?(cachedGlassMap(key)||lastMap):''
  useEffect(()=>{if(!active)return;let current=true;requestGlassMap(width,height,radius).then(value=>{if(current)setMap(value)}).catch(()=>{});return()=>{current=false}},[active,key])
  const definitions = map ? <svg className="glass-filter-defs" aria-hidden="true"><defs><filter id={id} x="0" y="0" width="100%" height="100%" colorInterpolationFilters="sRGB">
    <feImage href={map} width="100%" height="100%" preserveAspectRatio="none" result="lens-map" />
    <feDisplacementMap in="SourceGraphic" in2="lens-map" scale={-strength} xChannelSelector="R" yChannelSelector="G" />
  </filter></defs></svg> : null
  return { definitions, backdropFilter: !supported || mode === 'inherited' ? 'none' : map ? `url(#${id}) blur(.8px) saturate(1.18)` : 'blur(2px) saturate(1.12)' }
}

export function GlassLens(size: { width: number; height: number; radius: number }) {
  const optics = useGlassOptics(size)
  return <>{optics.definitions}<span className="glass-refraction" data-optics="svg" style={{ backdropFilter: optics.backdropFilter, clipPath: `inset(0 round ${Math.min(size.radius, size.width / 2, size.height / 2)}px)` }} /></>
}

/** Fixed hit target; glass and foreground share one directional deformation. */
function useLiquidMotion(disabled = false, thumb = false) {
  const { reduced } = useContext(AppearanceContext)
  const state = useRef({ hover: false, pressed: false, dragged: false, id: -1, dx: 0, dy: 0, px: 0, py: 0, startX: 0, startY: 0, at: 0, host: null as HTMLElement | null, rect: null as DOMRect | null, frame: 0, releaseTimer: 0 })
  const input=useRef<LiquidInput>({up:()=>{},cancel:()=>{}})
  input.current.up=event=>{if(state.current.pressed)release(event)};input.current.cancel=cancel
  const x = useSpring(0, liquidPhysics), y = useSpring(0, liquidPhysics)
  const a = useSpring(1, liquidPhysics), d = useSpring(1, liquidPhysics), b = useSpring(0, liquidPhysics)
  const transform = useMotionTemplate`matrix(${a}, ${b}, ${b}, ${d}, ${x}, ${y})`
  const lightX = useSpring(0, liquidPhysics), lightY = useSpring(0, liquidPhysics), light = useSpring(0, liquidPhysics), lift = useSpring(0, liquidPhysics)
  const lightTransform = useMotionTemplate`translate3d(${lightX}%, ${lightY}%, 0)`
  const boxShadow = useMotionTemplate`0 3px 8px rgb(34 45 65 / ${lift}), inset 0 1px 1px var(--rim-bright), inset 0 -1px 1px var(--rim-soft)`
  function update() {
    const s = state.current, active = !disabled && !reduced && (s.hover || s.pressed)
    let aa = 1, dd = 1, bb = 0, tx = 0, ty = 0
    if (active && thumb) { aa = s.pressed ? 1.18 : 1.08; dd = s.pressed ? 1.12 : 1.08 }
    else if (active && s.pressed) {
      const r = Math.hypot(s.dx, s.dy), q = 1 - Math.exp(-r / 32), ux = r ? s.dx / r : 0, uy = r ? s.dy / r : 0
      const along = .98 + .19 * q, across = .98 - .055 * q
      aa = r ? 1 + .8 * (across + (along - across) * ux * ux - 1) : .984
      dd = r ? 1 + .8 * (across + (along - across) * uy * uy - 1) : .984
      bb = .8 * (along - across) * ux * uy
      tx = 9.6 * Math.tanh(s.dx / 42); ty = 8 * Math.tanh(s.dy / 36)
    } else if (active) { aa = 1.018 + Math.abs(s.px) * .012; dd = 1.045; tx = s.px * 2.8; ty = s.py * 2 }
    if (!thumb && s.rect) {
      // Strain is limited by the actual dimensions, not a percentage of a long
      // row. Text keeps its native size and stays inside the same moving lens.
      const w = Math.max(1, s.rect.width), h = Math.max(1, s.rect.height)
      const ex = Math.abs(aa - 1) * w / 2 + Math.abs(bb) * h / 2
      const ey = Math.abs(dd - 1) * h / 2 + Math.abs(bb) * w / 2
      const gain = Math.min(1, 10 / Math.max(.001, ex), 7 / Math.max(.001, ey))
      aa = 1 + (aa - 1) * gain; dd = 1 + (dd - 1) * gain; bb *= gain
    }
    const set = (value: typeof x, next: number) => reduced ? value.jump(next) : value.set(next)
    set(x, tx); set(y, ty); set(a, aa); set(d, dd); set(b, bb)
    set(light, active ? s.pressed ? .6 : .38 : 0); set(lift, active ? .12 : 0)
    lightX.set(s.px * 30); lightY.set(s.py * 25)
  }
  function thumbContains(node: HTMLElement, cx: number, cy: number) {
    const input = node as HTMLInputElement, rect = state.current.rect || node.getBoundingClientRect()
    const fraction = (Number(input.value) - Number(input.min)) / (Number(input.max) - Number(input.min) || 1)
    return Math.hypot(cx - rect.left - sliderThumbSize / 2 - (rect.width - sliderThumbSize) * fraction, cy - rect.top - rect.height / 2) <= sliderThumbSize / 2 + 1
  }
  function point(event: PointerEvent<HTMLElement>) {
    const s = state.current
    if (s.pressed && event.pointerId !== s.id) return
    const rect = s.rect || (s.rect = event.currentTarget.getBoundingClientRect())
    s.host = event.currentTarget
    if (s.pressed && !thumb) { s.dx = event.clientX - s.startX; s.dy = event.clientY - s.startY; if (Math.hypot(s.dx, s.dy) > 5) { s.dragged = true; s.host.dataset.pulled = 'true' } }
    s.hover = event.pointerType !== 'touch' && (thumb ? thumbContains(event.currentTarget, event.clientX, event.clientY) : event.clientX >= rect.left && event.clientX <= rect.right && event.clientY >= rect.top && event.clientY <= rect.bottom)
    if(s.hover||s.pressed)liquidInput.add(input.current);else liquidInput.remove(input.current)
    s.px = Math.max(-1, Math.min(1, (event.clientX - rect.left) / rect.width * 2 - 1))
    s.py = Math.max(-1, Math.min(1, (event.clientY - rect.top) / rect.height * 2 - 1))
    if (!s.frame) s.frame = requestAnimationFrame(() => { s.frame = 0; update() })
  }
  function release(event?: globalThis.PointerEvent) {
    const s = state.current
    if (!s.pressed || (event && s.id !== event.pointerId)) return
    if (event && s.host && s.rect) {
      if (!thumb && Math.hypot(event.clientX - s.startX, event.clientY - s.startY) > 5) s.dragged = true
      s.hover = event.pointerType !== 'touch' && event.clientX >= s.rect.left && event.clientX <= s.rect.right && event.clientY >= s.rect.top && event.clientY <= s.rect.bottom
    }
    s.pressed = false; s.dx = 0; s.dy = 0; s.host?.removeAttribute('data-pressed'); s.host?.removeAttribute('data-pulled')
    if(!s.hover)liquidInput.remove(input.current)
    cancelAnimationFrame(s.frame); s.frame = 0
    const delay = reduced ? 0 : Math.max(0, 75 - (performance.now() - s.at))
    clearTimeout(s.releaseTimer); s.releaseTimer = window.setTimeout(update, delay)
  }
  function cancel() {
    const s = state.current
    liquidInput.remove(input.current)
    clearTimeout(s.releaseTimer); cancelAnimationFrame(s.frame); s.frame = 0
    s.hover = false; s.pressed = false; s.dragged = true; s.dx = 0; s.dy = 0
    s.host?.removeAttribute('data-pressed'); s.host?.removeAttribute('data-pulled'); update()
  }
  useEffect(() => {
    if (disabled) cancel(); else update()
    return () => { liquidInput.remove(input.current);clearTimeout(state.current.releaseTimer); cancelAnimationFrame(state.current.frame) }
  }, [disabled, reduced])
  return { style: { transform }, strain: { a, b, d, x, y }, thumbStyle: { transform, boxShadow }, highlight: { transform: lightTransform, opacity: light },
    enter: (event: PointerEvent<HTMLElement>) => { if (!state.current.pressed) state.current.rect = event.currentTarget.getBoundingClientRect(); point(event) }, move: point,
    press: (event?: PointerEvent<HTMLElement>) => {
      if (disabled || (event && event.button !== 0)) return
      const s = state.current; clearTimeout(s.releaseTimer); s.pressed = true; s.dragged = false; s.dx = 0; s.dy = 0; s.at = performance.now()
      liquidInput.add(input.current)
      if (event) {
        s.host = event.currentTarget; s.rect = event.currentTarget.getBoundingClientRect(); s.id = event.pointerId
        s.startX = event.clientX; s.startY = event.clientY; s.host.dataset.pressed = 'true'
        if (!thumb) event.currentTarget.setPointerCapture(event.pointerId)
        point(event)
      }
      update()
    }, release, cancel, wasDragged: () => state.current.dragged,
    lostCapture: () => { if (state.current.pressed) cancel() },
    leave: () => { state.current.hover = false; if (!state.current.pressed){state.current.rect = null;liquidInput.remove(input.current)} update() },
  }
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { radius?: number; nativeLayout?: boolean; pull?: boolean; optics?: OpticalMode }
export const GlassButton = forwardRef<HTMLButtonElement, ButtonProps>(function GlassButton({ children, className = '', radius, nativeLayout = false, pull = true, optics: requestedOptics = 'refractive', style, ...props }, forwarded) {
  const inheritedBackdrop = useContext(GlassBackdropContext)
  const bounded = props.role === 'option' || props.role === 'combobox' || /(?:^|\s)(nav-item|source-choice|episode-choice)(?:\s|$)/.test(className)
  const host = useRef<HTMLButtonElement>(null), liquid = useLiquidMotion(Boolean(props.disabled || !pull), false)
  const [size, setSize] = useState({ width: 0, height: 0, radius: 20 })
  // Disabled controls sit in an opacity group. Keep them out of SVG backdrop
  // displacement so nearby popovers cannot leave a second sampled outline.
  const opticalMode = inheritedBackdrop ? 'inherited' : props.disabled ? 'clear' : requestedOptics
  const optics = useGlassOptics(size, opticalMode)
  useImperativeHandle(forwarded, () => host.current!)
  useLayoutEffect(() => {
    // Inherited panes already supply the optical sample. Measuring every
    // button during a dialog mount forces layout without producing a map.
    if (opticalMode !== 'refractive') return
    const node = host.current; if (!node) return
    const update = () => {
      const material = node.querySelector<HTMLElement>(':scope > .glass-material')!
      const next = { width: material.offsetWidth, height: material.offsetHeight, radius: Math.min(parseFloat(getComputedStyle(node).borderTopLeftRadius) || 20, material.offsetHeight / 2, material.offsetWidth / 2) }
      setSize(old => old.width === next.width && old.height === next.height && old.radius === next.radius ? old : next)
    }
    const observer = new ResizeObserver(update)
    observer.observe(node); return () => observer.disconnect()
  }, [radius, className, opticalMode])
  return <button {...props} ref={host} type={props.type || 'button'} className={`glass-button liquid-control ${bounded ? 'glass-bounded' : ''} ${nativeLayout ? 'glass-native' : ''} ${className}`}
    style={{ ...style, ...(radius !== undefined ? { '--glass-radius': `${radius}px` } : {}) } as React.CSSProperties}
    onPointerEnter={e => { if (pull) liquid.enter(e); props.onPointerEnter?.(e) }} onPointerMove={e => { if (pull) liquid.move(e); props.onPointerMove?.(e) }}
    onPointerLeave={e => { liquid.leave(); props.onPointerLeave?.(e) }} onPointerDown={e => { if (pull) liquid.press(e); props.onPointerDown?.(e) }}
    onPointerUp={e => { liquid.release(e.nativeEvent); props.onPointerUp?.(e) }} onPointerCancel={e => { liquid.cancel(); props.onPointerCancel?.(e) }}
    onLostPointerCapture={e => { liquid.lostCapture(); props.onLostPointerCapture?.(e) }}
    onClickCapture={e => { if (e.detail > 0 && liquid.wasDragged()) { e.preventDefault(); e.stopPropagation() } else props.onClickCapture?.(e) }}
    onKeyDown={e => { if (pull && !e.repeat && [' ', 'Enter'].includes(e.key)) liquid.press(); props.onKeyDown?.(e) }}
    onKeyUp={e => { if ([' ', 'Enter'].includes(e.key)) liquid.release(); props.onKeyUp?.(e) }} onBlur={e => { liquid.cancel(); props.onBlur?.(e) }}>
    {opticalMode === 'clear' && <span className="glass-control-backdrop" aria-hidden="true" style={{backdropFilter:optics.backdropFilter}}/>}
    <motion.span className="glass-material" style={{...liquid.style, backdropFilter: opticalMode === 'clear' ? 'none' : optics.backdropFilter}} aria-hidden="true" data-optics={opticalMode}>
      {optics.definitions}<span className="glass-rim" /><span className="glass-sheen" /><span className="glass-highlight-clip"><motion.span className="glass-highlight" style={liquid.highlight} /></span>
    </motion.span>
    <motion.span className="glass-content" style={liquid.style}>{Children.map(children, child => typeof child === 'string' || typeof child === 'number' ? <span className="control-text">{child}</span> : child)}</motion.span>
  </button>
})

export function GlassSlider({ className = '', value, min = 0, max = 100, disabled, resetValue, ...props }: InputHTMLAttributes<HTMLInputElement> & {resetValue?:number}) {
  const pointerActive = useRef(false)
  const detent = resetValue === undefined ? null : Math.max(0,Math.min(1,(resetValue-Number(min))/(Number(max)-Number(min)||1)))
  const liquid = useLiquidMotion(disabled, true)
  const optics=useGlassOptics({width:sliderThumbSize,height:sliderThumbSize,radius:sliderThumbSize/2},'refractive',6)
  // Resize the sampling pane around its center instead of transforming its
  // backdrop coordinates. The rim and refraction then share the pressed bounds.
  const width=useTransform(liquid.strain.a,v=>sliderThumbSize*v),height=useTransform(liquid.strain.d,v=>sliderThumbSize*v)
  const left=useTransform(width,v=>(sliderThumbSize-v)/2),top=useTransform(height,v=>(sliderThumbSize-v)/2)
  const fraction = Math.max(0, Math.min(1, (Number(value) - Number(min)) / (Number(max) - Number(min) || 1)))
  return <div className={`glass-slider ${className}`}>
    <div className="glass-slider-rail" aria-hidden="true" style={{'--thumb-position':`${fraction*100}%`} as React.CSSProperties}><div className="glass-slider-track"><span style={{ width: `${fraction * 100}%` }} /></div>{detent!==null&&<span className="glass-slider-default" title={`默认值 ${resetValue}`} style={{left:`${detent*100}%`}}/>}</div>
    <div className="glass-slider-travel" aria-hidden="true"><span className="glass-thumb" style={{ left: `${fraction * 100}%` }}><motion.span className="glass-material" style={{width,height,left,top,inset:'auto',boxShadow:liquid.thumbStyle.boxShadow,backdropFilter:optics.backdropFilter}}>
      {optics.definitions}<span className="glass-rim" /><span className="glass-sheen" /><span className="glass-highlight-clip"><motion.span className="glass-highlight" style={liquid.highlight} /></span>
    </motion.span></span></div>
    <input {...props} type="range" value={value} min={min} max={max} disabled={disabled}
      onChange={e=>{if(pointerActive.current&&resetValue!==undefined){const span=Number(max)-Number(min);let radius=Math.min(span*.018,Math.max(Number(props.step||1)*2,span*2/Math.max(1,e.currentTarget.clientWidth-sliderThumbSize)));if(Number(min)<0)radius=Math.min(radius,.15);if(Math.abs(Number(e.currentTarget.value)-resetValue)<=radius)e.currentTarget.value=String(resetValue)}props.onChange?.(e)}}
      onPointerEnter={e => { liquid.enter(e); props.onPointerEnter?.(e) }} onPointerMove={e => { liquid.move(e); props.onPointerMove?.(e) }} onPointerLeave={e => { liquid.leave(); props.onPointerLeave?.(e) }}
      onPointerDown={e => { pointerActive.current=true; liquid.press(e); props.onPointerDown?.(e) }} onPointerUp={e => { pointerActive.current=false; liquid.release(e.nativeEvent); props.onPointerUp?.(e) }} onPointerCancel={e => { pointerActive.current=false; liquid.cancel(); props.onPointerCancel?.(e) }}
      onKeyDown={e => { if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'].includes(e.key)) liquid.press(); props.onKeyDown?.(e) }}
      onKeyUp={e => { liquid.release(); props.onKeyUp?.(e) }} onBlur={e => { pointerActive.current=false; liquid.cancel(); props.onBlur?.(e) }} />
  </div>
}

export function GlassSurface({ children, className = '', radius = 28, kind = 'toolbar', ...animation }: { children: ReactNode; className?: string; radius?: number; kind?: 'button' | 'toolbar' | 'popover' | 'dialog'; interactive?: boolean } & Pick<MotionProps, 'initial' | 'animate' | 'exit' | 'transition'>) {
  const host = useRef<HTMLDivElement>(null), [size, setSize] = useState({ width: 0, height: 0, radius })
  // A menu is a flat pane: sample once without displacing the underlying rows.
  // Its options reuse this pane instead of filtering an already filtered image.
  const optics = useGlassOptics(size, kind === 'popover' ? 'clear' : 'refractive')
  useLayoutEffect(() => {
    const node = host.current; if (!node) return
    const update = () => setSize(old => {
      const width = node.offsetWidth, height = node.offsetHeight, r = Math.min(radius, width / 2, height / 2)
      return old.width === width && old.height === height && old.radius === r ? old : { width, height, radius: r }
    })
    const observer = new ResizeObserver(update); observer.observe(node); return () => observer.disconnect()
  }, [radius])
  // The element that fades also owns the backdrop. An opacity wrapper around a
  // filtered child changes its backdrop root at opacity 1 and causes a flash.
  const backdropFilter=kind==='popover'&&optics.backdropFilter!=='none'?'blur(16px) saturate(1.12)':optics.backdropFilter
  return <motion.div {...animation} ref={host} className={`glass-surface glass-panel material-${kind} ${className}`} data-material={kind} style={{ '--glass-radius': `${radius}px`, backdropFilter } as React.CSSProperties}>{optics.definitions}<span className="glass-rim" aria-hidden="true"/><div className="glass-content">{children}</div></motion.div>
}
