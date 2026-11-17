'use strict'
module.exports=function(){
 const base=require('./fixture.cjs')(),selected=new Set(),covers=[],operations=[],libraryQueries=[];let loading=false
 const root='D:\\示例媒体',names=['星海旅行 第一季','午后日记 第一季','山间来信 第一季']
 // Use decoded bitmap covers, matching the real thumbnail format. SVG text
 // layout inside an image otherwise pollutes renderer frame-time measurements.
 const artwork=new Map(),image=(title,index)=>{const key=(title==='星海旅行'?'series':'episode')+'-'+(index%3);if(!artwork.has(key))artwork.set(key,'data:image/png;base64,'+require('node:fs').readFileSync(require('node:path').join(__dirname,'fixtures/covers',key+'.png')).toString('base64'));return artwork.get(key)}
 const episodes=(name)=>Array.from({length:12},(_,i)=>({id:`episode:${name}:${i}`,name:`第 ${i+1} 集 · 星海旅途`,filename:`S01E${String(i+1).padStart(2,'0')}.mkv`,folder:false,path:`${root}\\${name}\\BD\\S01E${i+1}.mkv`,keys:[`episode:${name}:${i}`],coverKey:`episode:${name}:${i}`,size:1234,modified:'2026-11-09 12:00',danmu:'已就绪',searchTitle:name,count:1,type:'动漫'}))
 const snap=()=>({...base.snap(),loading,selected:selected.size,qaCoverCalls:covers,qaOperations:operations,qaLibraryQueries:libraryQueries})
 return {snap,state:snap,async call(method,p={}){
  if(method==='state')return snap()
  if(method==='cover'){covers.push({...p});return image(p.series?'星海旅行':'分集封面',Number(p.key.split(':').at(-1))||0)}
  if(method==='library'){
   libraryQueries.push({...p})
   let rows=p.directory?episodes(p.directory.split('\\')[2]):names.map((name,i)=>({id:'folder:'+i,name,filename:name,folder:true,path:root+'\\'+name,coverKey:episodes(name)[0].id,keys:episodes(name).map(e=>e.id),size:12000,modified:'2026-11-09',danmu:'12 / 12 集已就绪',count:12,searchTitle:name}))
   if(p.directory===root+'\\'+names[0])rows=[{id:'folder:BD',name:'BD',folder:true,path:p.directory+'\\BD',coverKey:episodes(names[0])[0].id,keys:episodes(names[0]).map(e=>e.id),count:12,danmu:'12 / 12 集已就绪'}]
   const fields={0:'name',1:'modified',2:'size',3:'type',4:'bitrate',5:'danmu'},field=fields[p.sort]||'name'
   return rows.filter(r=>!p.filter||r.name.includes(p.filter)).map(r=>({...r,selected:r.keys.every(k=>selected.has(k)),mixed:r.keys.some(k=>selected.has(k))&&!r.keys.every(k=>selected.has(k))})).sort((a,b)=>(p.descending?-1:1)*String(a[field]??'').localeCompare(String(b[field]??''),'zh-CN',{numeric:true}))
  }
  if(method==='select'){p.keys.forEach(k=>p.selected?selected.add(k):selected.delete(k));return true}
  if(method==='selectClear'){selected.clear();return true}
  if(['refresh','scan','initialize','addLibrary','firewall'].includes(method)){if(method==='refresh'||method==='scan'){loading=true;setTimeout(()=>loading=false,80)}operations.push({method,...p});return method==='addLibrary'?{name:p.name}:true}
  return base.call(method,p)
 }}
}
