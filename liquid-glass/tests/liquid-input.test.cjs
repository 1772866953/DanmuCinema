const {test}=require('node:test'),assert=require('node:assert/strict')
test('only active liquid controls share global input listeners; unmount releases them',async()=>{
 const {createLiquidInput}=await import('../src/liquid-input.ts');const target=new EventTarget(),listeners=new Set(),add=target.addEventListener.bind(target),remove=target.removeEventListener.bind(target)
 target.addEventListener=(type,fn)=>{listeners.add(type);add(type,fn)};target.removeEventListener=(type,fn)=>{listeners.delete(type);remove(type,fn)}
 const hub=createLiquidInput(()=>target);let ups=0,cancels=0
 const first={up(){ups++;hub.remove(first)},cancel(){cancels++;hub.remove(first)}},second={up(){ups++},cancel(){cancels++;hub.remove(second)}}
 assert.equal(listeners.size,0);hub.add(first);hub.add(first);hub.add(second);assert.equal(listeners.size,3)
 target.dispatchEvent(new Event('pointerup'));assert.equal(ups,2);assert.equal(listeners.size,3)
 target.dispatchEvent(new Event('blur'));assert.equal(cancels,1);assert.equal(listeners.size,0)
 hub.add(first);target.dispatchEvent(new Event('pointercancel'));assert.equal(cancels,2);assert.equal(listeners.size,0)
 hub.add(first);hub.remove(first);target.dispatchEvent(new Event('pointerup'));assert.equal(ups,2);assert.equal(listeners.size,0)
})
