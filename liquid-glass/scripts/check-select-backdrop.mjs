import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),{PNG}=runtime('pngjs'),out=path.resolve('artifacts/select-backdrop');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const deviceScale=process.argv.find(x=>x.startsWith('--dpi='))?.split('=')[1]||'1.25'
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${deviceScale}`],env}),report={passed:false,deviceScale,cases:[],samples:[],errors:[]},check=(v,label)=>{assert(v,label);report.cases.push(label)}
function difference(a,b){a=PNG.sync.read(a);b=PNG.sync.read(b);assert(a.width===b.width&&a.height===b.height);let sum=0,count=0;for(let y=5;y<a.height-5;y++)for(let x=5;x<a.width-5;x++)for(let k=0;k<3;k++){const at=(y*a.width+x)*4+k;sum+=Math.abs(a.data[at]-b.data[at]);count++}return sum/count}
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show();w.focus()});await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.locator('.sidebar').getByRole('button',{name:'下载任务',exact:true}).click();await page.waitForTimeout(200)
 const trigger=page.getByRole('combobox',{name:'筛选任务'}),cancel=page.getByRole('button',{name:'取消并清理',exact:true})
 const modes=process.argv.includes('--diagnose')?[['current',''],['surface-css','.select-menu{backdrop-filter:blur(2px) saturate(1.12)!important}'],['single-layer','.select-menu{backdrop-filter:blur(2px) saturate(1.12)!important}.select-menu .glass-material{backdrop-filter:none!important}'],['all-css','.select-menu{backdrop-filter:blur(2px) saturate(1.12)!important}.select-menu .glass-material{backdrop-filter:none!important}.glass-material{backdrop-filter:blur(1px)!important}']]:[['fixed','']]
 for(const [name,css]of modes){const style=css?await page.addStyleTag({content:css}):null;await page.mouse.move(2,2);await page.waitForTimeout(250);const before=await cancel.screenshot();await trigger.click();await page.getByRole('listbox',{name:'筛选任务'}).waitFor();await page.waitForTimeout(250)
  const deltas=[];for(const option of await page.getByRole('option').all()){await option.hover();await page.waitForTimeout(180);deltas.push(difference(before,await cancel.screenshot()))}
  await page.screenshot({path:path.join(out,name+'-tasks.png')});
  await page.evaluate(()=>{const grid=document.createElement('div');grid.id='backdrop-grid';Object.assign(grid.style,{position:'fixed',inset:'0',zIndex:'999',pointerEvents:'none',background:'repeating-linear-gradient(to bottom,#375f4c 0px,#375f4c 32px,#d2ffe9 32px,#d2ffe9 44px)'});document.body.append(grid)})
  await page.mouse.move(2,2);await page.waitForTimeout(200);const geometry=await page.locator('.select-menu').evaluate(el=>({rect:el.getBoundingClientRect().toJSON(),w:innerWidth,h:innerHeight})),buffer=await page.screenshot({path:path.join(out,name+'-grid.png')}),im=PNG.sync.read(buffer),dpr=im.width/geometry.w,r=geometry.rect;
  const inside=[],outside=[],start=Math.ceil((r.y+24)*dpr),end=Math.floor((r.bottom-24)*dpr),ref=Math.round((r.x-20)*dpr),x1=Math.round((r.x+r.width*.55)*dpr),x2=Math.round((r.x+r.width*.7)*dpr)
  for(let y=start;y<end;y++){let sum=0;for(let x=x1;x<x2;x++)sum+=im.data[(y*im.width+x)*4+1];inside.push(sum/(x2-x1));outside.push(im.data[(y*im.width+ref)*4+1])}
  let best={shift:0,correlation:-Infinity};for(let shift=-12;shift<=12;shift++){const pairs=inside.map((v,i)=>[v,outside[i+shift]]).filter(v=>v[1]!==undefined),a=pairs.reduce((s,v)=>s+v[0],0)/pairs.length,b=pairs.reduce((s,v)=>s+v[1],0)/pairs.length;let cov=0,aa=0,bb=0;for(const [x,y]of pairs){cov+=(x-a)*(y-b);aa+=(x-a)**2;bb+=(y-b)**2}const correlation=cov/Math.sqrt(aa*bb);if(correlation>best.correlation)best={shift,correlation}}
  report.samples.push({name,cancelPixelDeltas:deltas,alignment:{...best,shiftCss:best.shift/dpr}});await page.evaluate(()=>document.getElementById('backdrop-grid').remove());await page.keyboard.press('Escape');await page.getByRole('listbox').waitFor({state:'detached'});if(style)await style.evaluate(el=>el.remove())
 }
 if(!process.argv.includes('--diagnose')){const s=report.samples[0];check(Math.abs(s.alignment.shiftCss)<=1,'菜单内外背景条纹处于同一水平线');check(Math.max(...s.cancelPixelDeltas)<2,'逐项悬停时，相邻取消按钮没有重复采样或重影')}
 if(!process.argv.includes('--diagnose')){
  await page.evaluate(()=>window.cinema.call('preferences',{scale:120,closeToTray:true}));await page.waitForTimeout(450)
  report.zoom120=[]
  for(let round=0;round<3;round++){
   await page.mouse.move(2,2);const before=await cancel.screenshot();await trigger.click();await page.getByRole('listbox',{name:'筛选任务'}).waitFor();await page.waitForTimeout(200)
   for(const option of await page.getByRole('option').all()){await option.hover();report.zoom120.push(difference(before,await cancel.screenshot()))}
   await page.keyboard.press('Escape');await page.getByRole('listbox').waitFor({state:'detached'})
  }
  check(Math.max(...report.zoom120)<2,'120%首次及重复展开，快速移入选项时取消按钮无重影')
  await trigger.click();await page.getByRole('listbox').waitFor();await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await page.getByRole('listbox').waitFor({state:'detached'});check((await trigger.innerText()).includes('进行中'),'菜单保留方向键选择与 Enter 确认')
  await page.locator('.sidebar').getByRole('button',{name:'首次设置',exact:true}).click();await page.getByRole('combobox',{name:'识别方式',exact:true}).click();await page.getByRole('listbox').waitFor();await page.waitForTimeout(200);await page.screenshot({path:path.join(out,`long-select-${deviceScale}.png`)});
  const layers=await page.locator('.select-menu').evaluate(el=>({filter:getComputedStyle(el).backdropFilter,inner:[...el.querySelectorAll('.glass-material')].map(m=>getComputedStyle(m).backdropFilter),rect:el.getBoundingClientRect().toJSON(),w:innerWidth,h:innerHeight}));check(!layers.filter.includes('url(')&&layers.inner.every(f=>f==='none'),'长下拉框只由菜单外壳采样背景，内部选项不重复折射');check(layers.rect.left>=0&&layers.rect.right<=layers.w+1&&layers.rect.top>=0&&layers.rect.bottom<=layers.h+1,'120%长下拉框保持在窗口边界内')
  await page.getByRole('option',{name:'电影',exact:true}).click();await page.getByRole('listbox').waitFor({state:'detached'});check((await page.getByRole('combobox',{name:'识别方式',exact:true}).innerText()).includes('电影'),'鼠标选择后正常更新值并关闭菜单')
 }
 check(report.errors.length===0,'本轮下拉菜单无 JavaScript 异常');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,`result-${deviceScale}.json`),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify(report,null,2))
