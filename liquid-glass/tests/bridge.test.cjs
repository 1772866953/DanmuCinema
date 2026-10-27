const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path')
const {CoreBridge}=require('../electron/bridge.cjs')
test('真实媒体核心：管道通信、持久化、缓存策略、白名单与退出',{timeout:30000},async()=>{
 const root=path.resolve('artifacts/core-test-'+Date.now());await fs.mkdir(root,{recursive:true})
 const create=()=>new CoreBridge(path.resolve('development/bin/installer-app/DanmuCinema.exe'),{DANMU_DATA_ROOT:root,DANMU_RUNTIME_ROOT:path.resolve('runtime/jellyfin')})
 let core=create()
 try{
  const initial=await core.ready;assert.equal(initial.running,false);assert.equal(initial.count,0);assert.equal(initial.settings.MediaFolder,'')
  assert(!('EncryptedToken' in initial.settings));assert(!('EncryptedDandanSecret' in initial.settings))
  await core.call('historyAdd',{scope:'danmu',text:'星海旅行'});assert.deepEqual(await core.call('history',{scope:'danmu'}),['星海旅行'])
  await core.call('historyAdd',{scope:'danmu',text:'山间来信'});await core.call('historyRemove',{scope:'danmu',text:'星海旅行'});assert.deepEqual(await core.call('history',{scope:'danmu'}),['山间来信'])
  await core.call('cacheRetention',{months:6});assert.equal((await core.call('state')).settings.CacheRetentionMonths,6)
  await assert.rejects(core.call('cacheRetention',{months:2}),/有效期/)
  await assert.rejects(core.call('executeShell',{command:'whoami'}),/未知操作/)
  assert.deepEqual(await core.call('library',{sort:0}),[])
  await core.call('logRetention',{days:14});assert.equal((await core.call('state')).settings.LogRetentionDays,14)
  await core.close();await new Promise(resolve=>core.child.once('exit',resolve))
  core=create();await core.ready
  assert.equal((await core.call('state')).settings.CacheRetentionMonths,6);assert.equal((await core.call('state')).settings.LogRetentionDays,14)
  assert.deepEqual(await core.call('history',{scope:'danmu'}),['山间来信'])
  await core.call('historyClear',{scope:'danmu'});assert.deepEqual(await core.call('history',{scope:'danmu'}),[])
 }finally{if(core.child.exitCode===null){await core.close().catch(()=>{});await new Promise(resolve=>core.child.once('exit',resolve))}}
})
