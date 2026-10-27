import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import {runtime} from './test-runtime.mjs'
const {_electron}=runtime('playwright'),out=path.resolve('artifacts/cold-launch');await fs.mkdir(out,{recursive:true})
const env={...process.env,DANMU_ELECTRON_USER_DATA:path.join(out,'profile-'+Date.now())};delete env.ELECTRON_RUN_AS_NODE
const app=await _electron.launch({executablePath:path.resolve('tools/electron/electron.exe'),args:['.'],env,timeout:30000})
try{const page=await app.firstWindow();const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.getByRole('heading',{name:'服务总览',exact:true}).waitFor();await page.waitForFunction(()=>!document.querySelector('.app-footer').textContent.includes('正在连接媒体核心'));const state=await page.evaluate(()=>window.cinema.call('state'));assert.equal(state.running,false);assert.equal(state.count,0);assert.equal(state.settings.MediaFolder,'');assert.equal(await page.locator('.qa-badge').count(),0);assert.equal(errors.length,0);await page.screenshot({path:path.join(out,'actual-cold-start.png')});await fs.writeFile(path.join(out,'result.json'),JSON.stringify({passed:true,running:state.running,mediaCount:state.count,blankMediaFolder:true,qa:false,errors},null,2));console.log('真实 Electron + 媒体核心冷启动通过，服务保持停止、媒体目录为空。')}finally{await app.close()}
