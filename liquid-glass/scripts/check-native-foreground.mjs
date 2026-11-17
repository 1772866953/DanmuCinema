import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/native-foreground'),dpi=process.argv.includes('--dpi=1.5')?'1.5':'1.25'
await fs.mkdir(out,{recursive:true});const hook=path.join(out,'login-mock.cjs');await fs.writeFile(hook,"module.exports=({app})=>{app.getLoginItemSettings=()=>({openAtLogin:false,launchItems:[]})}")
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now()),DANMU_QA_SCRIPT:hook};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${dpi}`],env})
const report={passed:false,dpi,cases:[],samples:[],errors:[]};let page
const check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',error=>report.errors.push(error.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()});await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
 const sample=button=>button.evaluate(el=>{const values=node=>{const m=new DOMMatrixReadOnly(getComputedStyle(node).transform);return[m.a,m.b,m.c,m.d,m.e,m.f]},content=el.querySelector('.glass-content'),style=getComputedStyle(content);return{material:values(el.querySelector('.glass-material')),content:values(content),filter:style.filter,backdrop:style.backdropFilter,smoothing:style.webkitFontSmoothing,font:style.font,opacity:style.opacity,canvas:el.querySelectorAll('canvas').length,rect:el.getBoundingClientRect().toJSON(),text:content.textContent}})
 const capture=async(button,name)=>{const r=await button.boundingBox();const png=await app.evaluate(async({BrowserWindow},r)=>{const w=BrowserWindow.getAllWindows()[0],z=w.webContents.getZoomFactor(),b=w.getContentBounds(),x=Math.max(0,Math.round((r.x-24)*z)),y=Math.max(0,Math.round((r.y-24)*z));return(await w.webContents.capturePage({x,y,width:Math.min(b.width-x,Math.round((r.width+48)*z)),height:Math.min(b.height-y,Math.round((r.height+48)*z))})).toPNG().toString('base64')},r);await fs.writeFile(path.join(out,name+'-'+dpi+'.png'),Buffer.from(png,'base64'))}
 const exercise=async(button,name,dx=50,dy=-24)=>{
  await page.mouse.move(2,2);await page.waitForTimeout(600);const original=await sample(button),r=await button.boundingBox();await capture(button,name+'-rest')
  await page.mouse.move(r.x+r.width/2,r.y+r.height/2);await page.waitForTimeout(700);const hover=await sample(button);report.samples.push({name,phase:'hover',...hover})
  check(hover.canvas===0&&hover.opacity==='1'&&hover.filter==='none'&&hover.backdrop==='none',name+'：悬停保持原生DOM文字与图标，无Canvas或滤镜替换')
  check(hover.content.every((v,i)=>Math.abs(v-hover.material[i])<.00001)&&hover.content[0]>1&&hover.content[3]>1,name+'：悬停前景与玻璃共用听页形变矩阵')
  check(hover.font===original.font&&hover.smoothing==='antialiased',name+'：悬停不切换字号字重与字体平滑方式')
  await capture(button,name+'-hover');await page.mouse.down();await page.mouse.move(r.x+r.width/2+dx,r.y+r.height/2+dy,{steps:8});await page.waitForTimeout(250)
  const drag=await sample(button);report.samples.push({name,phase:'drag',...drag});check(drag.content.every((v,i)=>Math.abs(v-drag.material[i])<.00001)&&Math.abs(drag.content[1])+Math.abs(drag.content[0]-1)+Math.abs(drag.content[3]-1)>.02,name+'：拖拽时整段前景和小图标实际发生同一方向形变')
  check(Math.abs(drag.rect.x-original.rect.x)<.1&&Math.abs(drag.rect.width-original.rect.width)<.1,name+'：命中区域稳定，不因前景变化抖动')
  if(await button.locator('.select-option-label').count()){
   const fits=await button.locator('.select-option-label').evaluate(el=>{const range=document.createRange();range.selectNodeContents(el);const a=range.getBoundingClientRect(),b=el.getBoundingClientRect();return a.left>=b.left-1&&a.right<=b.right+1&&a.top>=b.top-1&&a.bottom<=b.bottom+1});check(fits,name+'：排序文字与裁切容器共同变形，文字保持完整')
  }
  await capture(button,name+'-drag');await page.mouse.up();await page.mouse.move(2,2);await page.waitForTimeout(800);const restored=await sample(button);check(restored.content.every((v,i)=>Math.abs(v-[1,0,0,1,0,0][i])<.00001)&&restored.canvas===0,name+'：复位没有切回位图或残留缩放')
 }
 await exercise(page.locator('.hero').getByRole('button',{name:'浏览媒体库',exact:true}),'hero')
 check(await page.getByRole('heading',{name:'服务总览',exact:true}).isVisible(),'文字拖拽不误触进入媒体库')
 for(const [name,key]of [['caption-minimize','最小化'],['caption-maximize','最大化或还原'],['caption-close','关闭窗口']])await exercise(page.getByRole('button',{name:key,exact:true}),name,36,-15)
 check(await page.getByRole('heading',{name:'服务总览',exact:true}).isVisible(),'纯图标拖拽松手不会误最小化、最大化或关闭窗口')
 await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.getByRole('heading',{name:'影片与弹幕',exact:true}).waitFor();const sort=page.getByRole('combobox',{name:'排序方式',exact:true})
 await exercise(sort,'sort-up',0,-55);await exercise(sort,'sort-diagonal',-48,25)
 await exercise(page.getByRole('button',{name:'管理接口',exact:true}),'manage-sources')
 await page.evaluate(()=>window.cinema.call('preferences',{scale:120}));await page.waitForTimeout(400);await exercise(sort,'sort-120',48,-25)
 await page.evaluate(()=>window.cinema.call('preferences',{scale:100}));await page.waitForTimeout(350);await page.getByRole('button',{name:'管理接口',exact:true}).click();const dialog=page.getByRole('dialog',{name:'管理接口',exact:true});await dialog.waitFor();await page.waitForTimeout(750);await exercise(dialog.getByRole('button',{name:'保存并启用',exact:true}),'dialog-save');check(await dialog.isVisible(),'弹窗内拖拽不会误保存或退出')
 await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});await page.emulateMedia({reducedMotion:'reduce'});await sort.hover();await page.waitForTimeout(150);const reduced=await sample(sort);check(reduced.content.every((v,i)=>Math.abs(v-[1,0,0,1,0,0][i])<.001),'减少动态效果仍保持原生、静止前景')
 check(report.errors.length===0,'本轮文字、图标、裁切检查无渲染异常');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result-'+dpi+'.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,dpi}))
