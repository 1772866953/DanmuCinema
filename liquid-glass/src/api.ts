import {useEffect,useState,type Dispatch,type SetStateAction} from 'react'
export type Data = Record<string, any>
export function useReducedPreference(){const [reduced,setReduced]=useState(()=>matchMedia('(prefers-reduced-motion: reduce)').matches);useEffect(()=>{const query=matchMedia('(prefers-reduced-motion: reduce)'),update=()=>setReduced(query.matches);query.addEventListener('change',update);update();return()=>query.removeEventListener('change',update)},[]);return reduced}
export function useDraft<T>(key:string,value:T):[T,Dispatch<SetStateAction<T>>]{
 const [state,setState]=useState<T>(()=>{try{const stored=localStorage.getItem('cinema.ui.'+key);return stored===null?value:JSON.parse(stored)}catch{return value}})
 useEffect(()=>{try{localStorage.setItem('cinema.ui.'+key,JSON.stringify(state))}catch{}},[key,state]);return[state,setState]
}
declare global { interface Window { cinema: { call(method:string,params?:Data):Promise<any>;window(action:string):Promise<any>;system(action:string,value?:any):Promise<any>;onState(callback:(state:Data)=>void):()=>void;onWindow(callback:(state:Data)=>void):()=>void }; __qa?: {setState(state:Data):void} } }
export const initial:Data={status:'正在连接媒体核心…',running:false,gateway:false,count:0,selected:0,tasks:[],addresses:[],settings:{Port:8096,DanmuPort:9321,MediaFolder:'',LibraryName:'我的影片',LibraryType:'mixed',AdminName:'admin',CloseToTray:true,StartServicesOnLaunch:false,PreferOriginal:true,EnableDandan:true,EnableAnimeko:true,EnableBahamut:true,EnableExistingDanmu:true,AnimeOnly:true,CacheRetentionMonths:3,DownloadIntervalSeconds:3,UiScalePercent:100,LogRetentionDays:30,customApis:''},schedule:{active:false,remaining:'00:00:00',status:'尚未设置定时任务'}}
export async function rpc(method:string,params:Data={}) {return window.cinema.call(method,params)}
export function system(action:string,value?:any){return window.cinema.system(action,value)}
export const isQA=location.hash==='#qa'
