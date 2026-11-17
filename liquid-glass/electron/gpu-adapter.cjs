'use strict'
const run=require('node:util').promisify(require('node:child_process').execFile)
// Match the UI's hardware adapter without storing a machine-specific name in
// public settings. Software/unknown adapters leave mpv's native fallback intact.
function adapterName(info){
 const device=info?.gpuDevice?.find(d=>d.active)
 if(!device||![0x8086,0x10de,0x1002,0x5143].includes(device.vendorId))return null
 const name=device.deviceString?.trim()
 return name&&!/[\r\n\0]/.test(name)?name:null
}
function adapterProvider(app){
 let pending
 return ()=>{
  if(pending)return pending
  const request=(async()=>{try{return adapterName(await app.getGPUInfo('complete'))}catch{return null}})()
  pending=request;request.finally(()=>{if(pending===request)pending=null});return request
 }
}
async function resolveAdapter(executable,name,execute=run){
 if(!name)return null
 try{
  // mpv validates this option before trying its context fallback list. Check
  // the live DXGI enumeration so a dock/GPU change cannot make startup fail.
  const result=await execute(executable,['--no-config','--d3d11-adapter=help'],{windowsHide:true,timeout:3000,maxBuffer:64*1024})
  const names=[...(result.stdout+'\n'+result.stderr).matchAll(/description:\s*([^\r\n]+)/g)].map(m=>m[1].trim())
  return names.find(n=>n.toLowerCase()===name.toLowerCase())||null
 }catch{return null}
}
module.exports={adapterName,adapterProvider,resolveAdapter}
