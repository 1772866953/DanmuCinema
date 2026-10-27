import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/control-materials');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1.25'],env})
const report={passed:false,cases:[],errors:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
try{
const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()});await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForTimeout(300)
const nav=page.locator('.sidebar').getByRole('button',{name:'启动与偏好',exact:true});await nav.screenshot({path:path.join(out,'nav-default.png')});await nav.hover();await page.waitForTimeout(300);await nav.screenshot({path:path.join(out,'nav-hover.png')})
if(process.argv.includes('--diagnose')){
 for(const [name,css]of [['no-refraction','.glass-refraction{display:none!important}'],['no-rim','.glass-rim{display:none!important}'],['no-sheen','.glass-sheen{display:none!important}'],['blur-only','.glass-refraction{backdrop-filter:blur(.8px)!important}']]){const style=await page.addStyleTag({content:css});await page.waitForTimeout(100);await nav.screenshot({path:path.join(out,name+'.png')});await style.evaluate(el=>el.remove())}
 await page.locator('.sidebar').getByRole('button',{name:'连接设备',exact:true}).click();await page.waitForTimeout(350);report.copy=await page.getByRole('button',{name:'复制视频服务器地址',exact:true}).evaluate(el=>({host:el.getBoundingClientRect().toJSON(),material:el.querySelector('.glass-material').getBoundingClientRect().toJSON(),margin:getComputedStyle(el.querySelector('.glass-material')).margin,contentMargin:getComputedStyle(el.querySelector('.glass-content')).margin}));report.passed=true
}else{
 const m=await nav.evaluate(el=>getComputedStyle(el.querySelector('.glass-material')).backdropFilter);check(m.includes('url('),'玻璃材质直接承接折射，移除内部重复采样层')
 const {PNG}=runtime('pngjs'),box=await nav.boundingBox();report.hoverBands=[]
 for(let i=0;i<12;i++){
  if(i%3===0){await page.mouse.move(3,3);await page.waitForTimeout(80)}
  await page.mouse.move(box.x+box.width*(i%2?.85:.3),box.y+box.height*(i%2?.25:.6))
  const buffer=await nav.screenshot();await fs.writeFile(path.join(out,`hover-${String(i).padStart(2,'0')}.png`),buffer)
  const im=PNG.sync.read(buffer),pixel=(x,y,k)=>im.data[(y*im.width+x)*4+k];let band=0
  for(let y=4;y<im.height*.28;y++){let sum=0,count=0;for(let x=25;x<im.width-25;x++){for(let k=0;k<3;k++){sum+=(pixel(x,y-2,k)+pixel(x,y+2,k))/2-pixel(x,y,k);count++}}band=Math.max(band,sum/count)}
  report.hoverBands.push(band)
 }
 check(Math.max(...report.hoverBands)<8,'多次移入及移动鼠标时，按钮顶部未出现贯穿灰色条纹')
 await page.locator('.sidebar').getByRole('button',{name:'连接设备',exact:true}).click();await page.waitForTimeout(350);const copy=page.getByRole('button',{name:'复制视频服务器地址',exact:true});const shape=await copy.evaluate(el=>{const h=el.getBoundingClientRect(),m=el.querySelector('.glass-material').getBoundingClientRect(),c=el.querySelector('.glass-content').getBoundingClientRect();return{host:[h.width,h.height],material:[m.width,m.height],center:[c.x+c.width/2-h.x-h.width/2,c.y+c.height/2-h.y-h.height/2],margin:getComputedStyle(el.querySelector('.glass-material')).margin}});report.shape=shape;check(Math.abs(shape.host[0]-shape.host[1])<.1&&Math.abs(shape.material[0]-shape.material[1])<.1,'复制按钮外层与材质均为等宽高圆形');check(shape.center.every(x=>Math.abs(x)<.1)&&shape.margin==='0px','复制图标居中且材质无外部文字样式的间距');await copy.screenshot({path:path.join(out,'copy-circle.png')})
 await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click();await page.getByRole('button',{name:'管理接口',exact:true}).click();await page.waitForTimeout(80);await page.evaluate(()=>{window.iconFrames=[];const start=performance.now();function frame(){const e=document.querySelector('.source-mark svg'),r=e.getBoundingClientRect();window.iconFrames.push({x:r.x,y:r.y,width:r.width,height:r.height});if(performance.now()-start<600)requestAnimationFrame(frame)}requestAnimationFrame(frame)});await page.waitForTimeout(650);const icon=await page.evaluate(()=>window.iconFrames);report.icon=icon;check(['x','y','width','height'].every(k=>Math.max(...icon.map(f=>f[k]))-Math.min(...icon.map(f=>f[k]))<.05),'弹窗淡入与完成时，图标位置和大小无二次跳动');await page.screenshot({path:path.join(out,'sources.png')});await page.keyboard.press('Escape');await page.getByRole('dialog').waitFor({state:'detached'});
 await page.locator('.sidebar').screenshot({path:path.join(out,'sidebar.png')});check(report.errors.length===0,'相关界面无 JavaScript 异常');report.passed=true
}
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases,shape:report.shape},null,2))
