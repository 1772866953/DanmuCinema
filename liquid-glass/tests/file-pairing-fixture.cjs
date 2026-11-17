'use strict'
module.exports=function(){
 const base=require('./library-view-fixture.cjs')(),calls=[];let plan
 return {snap:()=>({...base.snap(),qaRenameCalls:calls}),async call(method,p={}){
  if(method==='state')return {...base.snap(),qaRenameCalls:calls}
  if(!method.startsWith('rename'))return base.call(method,p)
  calls.push({method,...p});const path=require('node:path').win32
  if(method==='renameFiles')return (p.files||Array.from({length:12},(_,i)=>path.join(p.directory,`${p.kind==='video'?'第':'原弹幕'} ${String(i+1).padStart(2,'0')} 集.${p.kind==='video'?'mkv':'xml'}`))).map(file=>({path:file,name:path.basename(file),size:100}))
  if(method==='renamePreview'){
   const changes=[],targets=new Set()
   p.danmu.forEach((file,i)=>{const video=p.videos[p.matches[i]];if(!video)return;const source=p.reverse?video:file,name=path.basename(p.reverse?file:video,path.extname(p.reverse?file:video))+path.extname(source),target=path.join(p.reverse||!p.placeWithVideo?path.dirname(source):path.dirname(video),name);if(targets.has(target))throw Error('多个文件将使用同一名称');targets.add(target);if(source!==target)changes.push({source,target})})
   return plan={token:'fixture-plan',changes,count:changes.length}
  }
  if(method==='renameApply'){if(p.token!==plan?.token)throw Error('预览已失效');const count=plan.count;plan=null;return count}
 }}
}
