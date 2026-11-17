import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),before=process.argv.includes('--before'),out=path.resolve('artifacts/lateral-refraction',before?'before':'after'),report={passed:false,cases:[],errors:[],captures:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--force-device-scale-factor=1'],env})
const check=(value,name)=>{assert(value,name);report.cases.push(name)}
try{
 const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',error=>report.errors.push(error.message))
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setPosition(0,0);w.show();w.focus()})
 await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.getByRole('tab',{name:'下载任务',exact:true}).click();await page.getByRole('heading',{name:'下载任务',exact:true}).waitFor()
 const rail=page.locator('.dock-hit-surface'),settle=()=>page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.motion==='settled')
 const capture=async name=>{
  // Capture dimensions and original glyph plane separately from the filtered lip.
  const data=await page.locator('.dock-lens').evaluate(el=>({rect:el.getBoundingClientRect().toJSON(),glyphTop:Math.min(...[...document.querySelectorAll('.dock-body>.dock-labels .dock-label:nth-child(3)>*')].map(n=>n.getBoundingClientRect().top)),glyphBottom:Math.max(...[...document.querySelectorAll('.dock-body>.dock-labels .dock-label:nth-child(3)>*')].map(n=>n.getBoundingClientRect().bottom)),viewport:{width:innerWidth,height:innerHeight,dpr:devicePixelRatio}}))
  const png=await app.evaluate(async({BrowserWindow})=>(await BrowserWindow.getAllWindows()[0].webContents.capturePage()).toPNG().toString('base64'));await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'));report.captures.push({name,...data})
 }
 for(const [width,height,scale]of [[1920,1080,100],[1920,1080,120],[2560,1440,100],[2560,1440,120]]){
  await app.evaluate(({BrowserWindow},size)=>{const w=BrowserWindow.getAllWindows()[0];w.setPosition(0,0);w.setSize(...size);w.focus()},[width,height]);await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.bringToFront();await page.waitForTimeout(700);await settle();await page.mouse.up()
  const name=`${width}-${scale}`,tab=await page.getByRole('tab',{name:'下载任务',exact:true}).boundingBox()
  await page.mouse.move(tab.x+tab.width/2,tab.y+tab.height/2);await page.mouse.down();await page.mouse.move(tab.x+tab.width*.56,tab.y+tab.height/2-2,{steps:20});await page.waitForTimeout(400)
  check(await rail.getAttribute('data-held')==='true'&&Number(await rail.getAttribute('data-lift'))>.95,name+': native pointer drag raises the lens')
  await capture(name+'-held')
  if(!before){
   check(await page.locator('.dock-raised-rim rect').evaluateAll(nodes=>nodes.length===2&&nodes.every(n=>!n.hasAttribute('x')&&!n.hasAttribute('y'))&&nodes[0].getAttribute('rx')===nodes[1].getAttribute('rx')),name+': both reference strokes share one exterior contour')
   check(await page.locator('.dock-prism-composite .dock-recording').count()===1&&await page.locator('.dock-lens-material').count()===0,name+': composite input replaces separate opaque lens tint')
   check(await page.locator('.dock-lens-clip').evaluate(el=>getComputedStyle(el).filter==='none'&&getComputedStyle(el).backdropFilter==='none'),name+': central glyphs stay unfiltered')
   check(await page.locator('.dock-lens-glass feFuncG').evaluate(el=>el.getAttribute('slope')==='1'&&Number(el.getAttribute('intercept'))<0),name+': reference normal field has an exact neutral centre')
  }
  if(width===1920&&scale===100){
   for(const mode of ['all','icons','text','empty']){
    await page.evaluate(mode=>{let style=document.getElementById('lip-test');if(!style){style=document.createElement('style');style.id='lip-test';document.head.append(style)}style.textContent='.dock-optic-environment{background:#666!important}.dock-lens-material{background:#444!important}'+(mode==='icons'?'.dock-label>span{visibility:hidden}':mode==='text'?'.dock-label>svg{visibility:hidden}':mode==='empty'?'.dock-labels{visibility:hidden}':'')},mode)
    await page.waitForTimeout(120);await capture('neutral-'+mode)
   }
   await page.evaluate(()=>document.getElementById('lip-test')?.remove())
  }
  await page.mouse.move(tab.x+tab.width*.95,tab.y+tab.height/2-2,{steps:20});await page.waitForTimeout(250);await capture(name+'-crossed')
  if(width===1920&&scale===100){
   for(const mode of ['all','icons','text','empty']){
    await page.evaluate(mode=>{let style=document.getElementById('lateral-test');if(!style){style=document.createElement('style');style.id='lateral-test';document.head.append(style)}style.textContent='.dock-optic-environment{background:#666!important}.dock-lens-material{background:#444!important}'+(mode==='icons'?'.dock-label>span{visibility:hidden}':mode==='text'?'.dock-label>svg{visibility:hidden}':mode==='empty'?'.dock-labels{visibility:hidden}':'')},mode)
    await page.waitForTimeout(120);await capture('crossed-'+mode)
   }
   await page.evaluate(()=>document.getElementById('lateral-test')?.remove())
   // Move a real cap across an actual icon stroke; the prior pose may cover
   // only letters, in which case the icons-only probe correctly stays empty.
   await page.evaluate(()=>{const s=document.createElement('style');s.id='icon-crossing-probe';s.textContent='.dock-optic-environment{background:#666!important}.dock-lens-material{background:#444!important}.dock-label>span{visibility:hidden}';document.head.append(s)})
   let pointerX=tab.x+tab.width*.95
   for(let align=0;align<2;align++){
    const shift=await page.evaluate(()=>document.querySelector('.dock-body>.dock-labels .dock-label:nth-child(4)>svg').getBoundingClientRect().left-5-document.querySelector('.dock-lens').getBoundingClientRect().left)
    pointerX+=shift;await page.mouse.move(pointerX,tab.y+tab.height/2-2,{steps:12});await page.waitForTimeout(180)
   }
   for(const offset of [-6,-3,0,3,6]){await page.mouse.move(pointerX+offset,tab.y+tab.height/2-2,{steps:8});await page.waitForTimeout(130);await capture('icon-crossing-'+(offset+6))}
   await page.evaluate(()=>document.getElementById('icon-crossing-probe')?.remove())
  }
  await page.mouse.move(tab.x+tab.width/2,tab.y+tab.height/2,{steps:16})
  await page.mouse.up();await settle();check(await page.getByRole('heading',{name:'下载任务',exact:true}).isVisible(),name+': release retains the selected scene')
 }
 check(report.errors.length===0,'lateral refraction has no renderer errors');report.passed=true
}catch(error){report.errors.push(error.stack);throw error}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));await app.close()}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,captures:report.captures.length}))
