import {useEffect,useRef,useState} from 'react'
import {GlassSlider} from './glass'
import {Field,Input} from './ui'

export function Continuous({label,value,min,max,step=.1,onCommit,disabled=false,hint,resetValue,unit=''}:{label:string;resetValue?:number;value:number;min:number;max:number;step?:number;onCommit(value:number):void;disabled?:boolean;hint?:string;unit?:string}){
 const [draft,setDraft]=useState(String(value)),dirty=useRef(false)
 useEffect(()=>{if(!dirty.current)setDraft(String(value))},[value])
 const commit=(raw:string)=>{if(!dirty.current)return;dirty.current=false;const n=Number(raw);if(!raw.trim()||!Number.isFinite(n)){setDraft(String(value));return}const next=Number((min+Math.round((Math.max(min,Math.min(max,n))-min)/step)*step).toFixed(3));setDraft(String(next));onCommit(next)}
 return <Field label={label}><div className="numeric-setting" title={hint}><GlassSlider resetValue={resetValue} aria-label={label} min={min} max={max} step={step} value={Math.max(min,Math.min(max,Number(draft)||0))} disabled={disabled} onChange={e=>{dirty.current=true;setDraft(e.target.value)}} onPointerUp={e=>commit(e.currentTarget.value)} onKeyUp={e=>commit(e.currentTarget.value)} onBlur={e=>commit(e.currentTarget.value)} onPointerCancel={()=>{dirty.current=false;setDraft(String(value))}}/><div className="numeric-setting-value"><Input aria-label={`${label}数值`} type="number" min={min} max={max} step={step} value={draft} disabled={disabled} onChange={e=>{dirty.current=true;setDraft(e.target.value)}} onBlur={e=>commit(e.currentTarget.value)} onKeyDown={e=>{if(e.key==='Enter')commit(e.currentTarget.value)}}/>{unit&&<span className="numeric-unit">{unit}</span>}</div></div></Field>
}
export function Stepped({label,value,options,onCommit,disabled=false,resetValue,hint,format}:{label:string;value:number;options:readonly {value:number;label:string}[];onCommit(value:number):void;disabled?:boolean;resetValue?:number;hint?:string;format?(value:number):string}){
 const nearest=(n:number)=>options.reduce((best,item,index)=>Math.abs(item.value-n)<Math.abs(options[best].value-n)?index:best,0)
 const [index,setIndex]=useState(()=>nearest(value)),[displayValue,setDisplayValue]=useState(value),dirty=useRef(false)
 const signature=options.map(item=>`${item.value}:${item.label}`).join('|')
 useEffect(()=>{if(!dirty.current){setIndex(nearest(value));setDisplayValue(value)}},[value,signature])
 const commit=(raw:string)=>{if(!dirty.current)return;dirty.current=false;const next=Math.max(0,Math.min(options.length-1,Math.round(Number(raw))));setIndex(next);setDisplayValue(options[next].value);onCommit(options[next].value)}
 const current=options.find(item=>item.value===displayValue)?.label||format?.(displayValue)||String(displayValue)
 return <Field label={label}><div className="numeric-setting stepped-setting" title={hint}><div className="stepped-track"><GlassSlider resetValue={resetValue===undefined?undefined:nearest(resetValue)} aria-label={label} aria-valuetext={current} min={0} max={options.length-1} step={1} value={index} disabled={disabled} onChange={e=>{dirty.current=true;const n=Number(e.target.value);setIndex(n);setDisplayValue(options[n].value)}} onPointerUp={e=>commit(e.currentTarget.value)} onKeyUp={e=>commit(e.currentTarget.value)} onBlur={e=>commit(e.currentTarget.value)} onPointerCancel={()=>{dirty.current=false;setIndex(nearest(value));setDisplayValue(value)}}/><div className="stepped-ticks" aria-hidden="true">{options.map((item,i)=><span key={item.value} title={item.label} style={{left:`${i/(options.length-1)*100}%`}}/>)}</div></div><output className="slider-value" aria-label={`${label}当前值`}>{current}</output></div></Field>
}
