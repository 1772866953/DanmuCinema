import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'

// Only exercise affected typography and shared pointer motion, using demo data.
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/button-typography')
await fs.mkdir(out,{recursive:true})
const report={passed:false,cases:[],samples:[],errors:[]}
const check=(ok,name)=>{assert(ok,name);report.cases.push(name)}
try{
 for(const dpi of ['1','1.25','1.5']){
  const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,`profile-${dpi}-${Date.now()}`)};delete env.ELECTRON_RUN_AS_NODE
  const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa',`--force-device-scale-factor=${dpi}`],env})
  try{
   const page=await app.firstWindow();page.setDefaultTimeout(12000);page.on('pageerror',e=>report.errors.push(e.message))
   await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.setSize(1420,960);w.show()})
   await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor()
   check(await page.evaluate(()=>document.fonts.check('500 12px "DanmuCinema UI"','搜索弹幕联合搜索')),`DPR ${dpi}: bundled font ready before app layout`)
   const cdp=await page.context().newCDPSession(page);await cdp.send('DOM.enable');await cdp.send('CSS.enable')
   await page.locator('.sidebar').getByRole('button',{name:'影片与弹幕',exact:true}).click()
   const sample=async(button,phase,name,scale)=>{
    const value=await button.evaluate(el=>{
     el.dataset.typographyProbe='true'
     const content=el.querySelector('.glass-content'),text=el.querySelector('.control-text'),cs=getComputedStyle(content),ts=getComputedStyle(text),range=document.createRange();range.selectNodeContents(text)
     return{text:text.textContent,font:ts.font,fontSize:ts.fontSize,family:ts.fontFamily,transform:cs.transform,material:getComputedStyle(el.querySelector('.glass-material')).transform,filter:cs.filter,backdrop:cs.backdropFilter,canvas:el.querySelectorAll('canvas').length,children:text.childElementCount,rect:el.getBoundingClientRect().toJSON(),textRect:range.getBoundingClientRect().toJSON()}
    })
    const {root}=await cdp.send('DOM.getDocument'),{nodeId}=await cdp.send('DOM.querySelector',{nodeId:root.nodeId,selector:'[data-typography-probe] .control-text'})
    const {fonts}=await cdp.send('CSS.getPlatformFontsForNode',{nodeId})
    check(fonts.length===1&&fonts[0].isCustomFont&&fonts[0].familyName==='Noto Sans SC'&&fonts[0].glyphCount===4,`${dpi}/${scale}/${name}/${phase}: all four characters use one bundled font`)
    check(value.canvas===0&&value.children===0&&value.filter==='none'&&value.backdrop==='none',`${dpi}/${scale}/${name}/${phase}: native continuous text, no per-character sizing or filters`)
    check(value.transform===value.material,`${dpi}/${scale}/${name}/${phase}: native foreground shares glass deformation`)
    await button.evaluate(el=>delete el.dataset.typographyProbe)
    report.samples.push({dpi,scale,name,phase,fonts,...value})
    return value
   }
   const capture=async(button,name)=>{
    const r=await button.boundingBox();const png=await app.evaluate(async({BrowserWindow},r)=>{const w=BrowserWindow.getAllWindows()[0],z=w.webContents.getZoomFactor();return(await w.webContents.capturePage({x:Math.round((r.x-22)*z),y:Math.round((r.y-22)*z),width:Math.round((r.width+44)*z),height:Math.round((r.height+44)*z)})).toPNG().toString('base64')},r)
    await fs.writeFile(path.join(out,name+'.png'),Buffer.from(png,'base64'))
   }
   for(const scale of [100,120]){
    await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale)
    for(const name of ['搜索弹幕','联合搜索']){
     if(name==='联合搜索'){await page.getByRole('button',{name:'搜索弹幕',exact:true}).click();await page.getByRole('dialog',{name:'选择弹幕来源',exact:true}).waitFor();await page.waitForTimeout(650)}
     const button=page.getByRole('button',{name,exact:true});await page.mouse.move(2,2);await page.waitForTimeout(450)
     const rest=await sample(button,'rest',name,scale);await capture(button,`${dpi}-${scale}-${name}-rest`)
     const r=await button.boundingBox();await button.hover();await page.waitForTimeout(500)
     const hover=await sample(button,'hover',name,scale);await capture(button,`${dpi}-${scale}-${name}-hover`)
     check(rest.font===hover.font,`${dpi}/${scale}/${name}: first hover does not swap font, size or weight`)
     check(Math.abs(rest.rect.width-hover.rect.width)<.1,`${dpi}/${scale}/${name}: hover hit target remains fixed`)
     await page.mouse.down();await page.mouse.move(r.x+r.width/2+42,r.y+r.height/2-20,{steps:6});await page.waitForTimeout(300)
     const drag=await sample(button,'drag',name,scale);await capture(button,`${dpi}-${scale}-${name}-drag`)
     check(rest.font===drag.font,`${dpi}/${scale}/${name}: drag keeps same glyph size and font`)
     check(drag.textRect.left>drag.rect.left-8&&drag.textRect.right<drag.rect.right+16,`${dpi}/${scale}/${name}: dragged label remains within padded lens`)
     await page.mouse.up();await page.mouse.move(2,2);await page.waitForTimeout(550)
     if(name==='联合搜索'){await page.keyboard.press('Escape');await page.getByRole('dialog',{name:'选择弹幕来源',exact:true}).waitFor({state:'detached'})}
     else check(await page.getByRole('dialog').count()===0,'text drag does not trigger search')
    }
   }
  }finally{await app.close()}
 }
 check(report.errors.length===0,'no renderer errors');report.passed=true
}catch(e){report.errors.push(e.stack);throw e}finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2))}
console.log(JSON.stringify({passed:report.passed,cases:report.cases.length,samples:report.samples.length}))
