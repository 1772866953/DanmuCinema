const {test}=require('node:test'),assert=require('node:assert/strict')
test('revisiting warm pages avoids storage IPC while real edits survive renderer recreation',async()=>{
 const {createDraftCache}=await import('../src/draft-cache.ts');const values=new Map([['page','"overview"']]);let reads=0,writes=0
 const storage=()=>({getItem(key){reads++;return values.get(key)??null},setItem(key,value){writes++;values.set(key,value)}}),cache=createDraftCache(storage)
 for(let i=0;i<30;i++){assert.equal(cache.read('page','library'),'overview');cache.write('page','overview')}
 assert.equal(reads,1);assert.equal(writes,0);cache.write('page','schedule');assert.equal(writes,1)
 assert.equal(createDraftCache(storage).read('page','overview'),'schedule')
 values.delete('page');cache.invalidate('page');assert.equal(cache.read('page','overview'),'overview')
})
test('unavailable or malformed storage keeps defaults and a failed write can be retried',async()=>{
 const {createDraftCache}=await import('../src/draft-cache.ts');let locked=true,text='bad JSON'
 const cache=createDraftCache(()=>({getItem(){return text},setItem(_,value){if(locked)throw Error('locked');text=value}}))
 assert.equal(cache.read('zoom',1),1);cache.write('zoom',1.2);assert.equal(text,'bad JSON');locked=false;cache.write('zoom',1.2);assert.equal(text,'1.2');assert.equal(cache.read('zoom',1),1.2)
})
