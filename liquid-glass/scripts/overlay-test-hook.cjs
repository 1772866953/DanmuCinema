'use strict'
// Reuse the existing native-player behavior checks against the shipped overlay
// integration. All profiles, media links and screenshots remain in test artifacts.
require('./foreground-test-guard.cjs')()
const path=require('node:path'),fs=require('node:fs/promises'),playerModule=require('../electron/player.cjs'),{PlayerOverlay}=require('../electron/overlay-host.cjs')
const NativePlayer=playerModule.LocalPlayer
class IntegratedPlayer extends NativePlayer{
 constructor(options){super({...options,createOverlay:value=>new PlayerOverlay({...value,base:process.cwd(),executable:path.resolve('tools/electron/electron.exe')})})}
 async open(...args){
  const result=await super.open(...args),ipc=this.ipc,command=ipc.command.bind(ipc)
  ipc.command=async(args,...rest)=>{
   const value=await command(args,...rest)
   if(args[0]==='screenshot-to-file'&&args[2]==='window'&&this.overlay){
    const {runtime}=await import('./test-runtime.mjs'),sharp=runtime('sharp'),base=await fs.readFile(args[1]),overlay=await fs.readFile(await this.overlay.capture()),a=await sharp(base).metadata(),b=await sharp(overlay).metadata()
    const layer=await sharp(overlay).extract({left:0,top:0,width:Math.min(a.width,b.width),height:Math.min(a.height,b.height)}).png().toBuffer()
    await sharp(base).composite([{input:layer,left:0,top:0}]).png().toFile(args[1])
   }
   return value
  }
  return result
 }
}
playerModule.LocalPlayer=IntegratedPlayer
