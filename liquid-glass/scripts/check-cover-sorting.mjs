import requireForegroundTest from './foreground-test-guard.cjs'
requireForegroundTest()
import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/cover-sorting-'+Date.now()),report={passed:false,checks:[],errors:[]}
await fs.mkdir(out,{recursive:true});const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile'),DANMU_QA_FIXTURE:path.resolve('tests/library-view-fixture.cjs')};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.','--qa','--enable-features=SkiaGraphite'],env,timeout:30000})
try{
 const page=await app.firstWindow();page.on('pageerror',e=>report.errors.push(e.message));await page.waitForFunction(()=>document.querySelector('.dock-hit-surface')?.dataset.ready==='true')
 await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];w.show();w.focus()});await page.getByRole('tab',{name:'影片与弹幕',exact:true}).click();await page.locator('.poster-card').first().waitFor()
 const latest=async()=>page.evaluate(async()=>{const s=await window.cinema.call('state');return s.qaLibraryQueries.at(-1)})
 for(const [label,key]of [['类型',3],['大小',2],['修改时间',1],['弹幕',5],['名称',0]]){
  const button=page.getByRole('group',{name:'封面排序'}).getByRole('button',{name:label+'排序',exact:true});await button.click();await page.waitForFunction(async key=>{const s=await window.cinema.call('state'),q=s.qaLibraryQueries.at(-1);return q.sort===key&&q.descending===false},key)
  assert.equal(await button.getAttribute('aria-pressed'),'true');await button.click();await page.waitForFunction(async key=>{const s=await window.cinema.call('state'),q=s.qaLibraryQueries.at(-1);return q.sort===key&&q.descending===true},key);report.checks.push(label+' supports ascending and descending')
 }
 await page.waitForFunction(()=>document.querySelector('.poster-description>strong')?.textContent==='星海旅行 第一季');const desc=await page.locator('.poster-description>strong').allTextContents();await page.getByRole('button',{name:'名称排序',exact:true}).click();await page.waitForFunction(expected=>[...document.querySelectorAll('.poster-description>strong')].map(e=>e.textContent).join('|')===expected.join('|'),[...desc].reverse());const asc=await page.locator('.poster-description>strong').allTextContents();assert.deepEqual(asc,[...desc].reverse());report.checks.push('cover order reverses visually')
 await page.getByRole('button',{name:'大小排序',exact:true}).click();await page.getByRole('button',{name:'大小排序',exact:true}).click();await page.waitForTimeout(180);await page.getByRole('button',{name:'列表视图',exact:true}).click();assert.equal(await page.getByRole('group',{name:'封面排序'}).count(),0);assert.equal(await page.locator('th[aria-sort="descending"] .table-sort').textContent(),'大小 / MB');await page.getByRole('button',{name:'大小 / MB排序',exact:true}).click();await page.waitForTimeout(180);await page.getByRole('button',{name:'封面视图',exact:true}).click();assert.equal(await page.getByRole('button',{name:'大小排序',exact:true}).getAttribute('aria-pressed'),'true');assert.equal((await latest()).descending,false);report.checks.push('list and cover share field and direction')
 for(const [width,height,scale]of [[1320,880,100],[1024,768,120],[940,640,120]]){
  await app.evaluate(({BrowserWindow},size)=>BrowserWindow.getAllWindows()[0].setSize(...size),[width,height]);await page.evaluate(scale=>window.cinema.call('preferences',{scale}),scale);await page.waitForTimeout(300)
  const layout=await page.locator('.cover-sorting').evaluate(e=>({bar:e.getBoundingClientRect().toJSON(),buttons:[...e.querySelectorAll('button')].map(b=>b.getBoundingClientRect().toJSON()),scroll:document.documentElement.scrollWidth,viewport:innerWidth,gallery:document.querySelector('.media-gallery').getBoundingClientRect().toJSON()}));assert(layout.scroll<=layout.viewport+1);assert(layout.buttons.every(b=>b.left>=layout.bar.left-1&&b.right<=layout.bar.right+1&&b.bottom<=layout.bar.bottom+1));assert(layout.gallery.height>100);report.checks.push(`${width}x${height} ${scale}% fits`)
 }
 await page.screenshot({path:path.join(out,'cover-sort.png')});assert.equal(report.errors.length,0);report.passed=true
}catch(error){report.error=error.stack;process.exitCode=1}finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2));await app.close();console.log(JSON.stringify({out,...report}))}
