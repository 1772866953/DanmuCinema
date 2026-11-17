// Every temporary warmup must be removed before the window becomes interactive.
// The shorter tray path used to finish while the form and lens warmups were
// still mounting or removing themselves on independent idle/timer callbacks.
const completed=new Set<string>(),resolve=new Map<string,()=>void>()
const stages=new Map(['player-form','slider-optics','page-materials',...(import.meta.env.VITE_DANMU_RETAIN_SOURCE_FORM!=='0'?['sources-form']:[])].map(role=>[role,new Promise<void>(done=>resolve.set(role,done))]))
export const startupWarmupReady=Promise.all(stages.values())
export const waitStartupWarmup=(role:string)=>stages.get(role)??Promise.resolve()
export function finishStartupWarmup(role:string){if(!completed.has(role)){completed.add(role);resolve.get(role)?.();resolve.delete(role)}}
export const afterPaint=()=>new Promise<void>(done=>requestAnimationFrame(()=>requestAnimationFrame(()=>done())))
