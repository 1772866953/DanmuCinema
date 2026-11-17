const {test}=require('node:test'),assert=require('node:assert/strict')
test('media menus keep the last command reachable at viewport edges and under UI zoom',async()=>{
 const {fitFloatingMenu}=await import('../src/floating-placement.ts')
 for(const [w,h]of [[1920,1080],[940,640],[940/1.2,640/1.2],[320,240]])for(const [x,y]of [[0,0],[w-1,h-1],[w/2,h/2]]){
  const p=fitFloatingMenu(x,y,210,306,w,h)
  assert(p.left>=12&&p.top>=12);assert(p.left+Math.min(210,p.maxWidth)<=w-12+.001);assert(p.top+Math.min(306,p.maxHeight)<=h-12+.001)
 }
})
