import test from 'node:test'
import assert from 'node:assert/strict'
import {translate,multiply,inverse,centered,rimNormal,dockPrism,capsulePath,glassWavelengths,glassEdgeMask,glassSourceMatrix} from '../src/dock-geometry.ts'
const near=(a,b)=>assert(Math.abs(a-b)<1e-8,`${a} != ${b}`)
test('ambient coordinates cancel both rail and lens transforms',()=>{
 for(const a of [[1.06,.004,.004,1.09,-5,3],[1,0,0,1,0,0],[1.03,-.006,-.006,1.05,5,-2]]){
  const world=multiply(multiply(multiply(translate(70,760),centered(a,590,30)),translate(-30,-10)),centered([1.18,.03,.03,.88,0,0],85,36))
  const padded=multiply(world,translate(-12,-12)),roundTrip=multiply(padded,inverse(padded));roundTrip.forEach((n,i)=>near(n,[1,0,0,1,0,0][i]))
 }
})
test('both cap normals use actual raised dimensions and turn inward when displaced',()=>{
 for(const [w,h] of [[130,52],[166,73],[85,64]]){
  const left=rimNormal(0,h/2,w,h),right=rimNormal(w,h/2,w,h),top=rimNormal(w/2,0,w,h)
  near(left.nx,-1);near(left.ny,0);near(right.nx,1);near(top.ny,-1);near(left.strength,1)
  const r=h/2,x=r-r/Math.sqrt(2),y=r-r/Math.sqrt(2),corner=rimNormal(x,y,w,h)
  near(corner.nx,-1/Math.sqrt(2));near(corner.ny,-1/Math.sqrt(2));near(corner.strength,1)
  assert(-dockPrism.bend*left.nx>0&&-dockPrism.bend*right.nx<0)
 }
})
test('refraction vanishes in the centre and has bounded continuous rim falloff',()=>{
 assert.equal(rimNormal(65,26,130,52).strength,0)
 const samples=Array.from({length:12},(_,x)=>rimNormal(x,26,130,52).strength)
 assert(samples.every((x,i)=>x>=0&&x<=1&&(!i||x<=samples[i-1])));assert.equal(samples.at(-1),0)
})
test('reference edge profile samples coherent colour and retains input overscan',()=>{
 assert.equal(dockPrism.edgeWidth,10);assert.equal(dockPrism.bend,8);assert(dockPrism.padding>dockPrism.bend)
 assert.equal('red' in dockPrism,false);assert.equal('blue' in dockPrism,false)
 assert(capsulePath(10,10,100,52).includes('A26 26'));assert(capsulePath(0,0,50,50).includes('A25 25'))
})
test('large panels keep their own corner radius instead of capsule ends',()=>{
 const w=630,h=520,r=26
 near(rimNormal(0,120,w,h,r).nx,-1);near(rimNormal(0,120,w,h,r).strength,1)
 near(rimNormal(100,0,w,h,r).ny,-1);assert.equal(rimNormal(100,100,w,h,r).strength,0)
 const corner=r-r/Math.sqrt(2),v=rimNormal(corner,corner,w,h,r);near(v.nx,-1/Math.sqrt(2));near(v.ny,-1/Math.sqrt(2))
})

test('raised wavelength samples remain inside the opaque overscan',()=>{
 assert.deepEqual(glassWavelengths,[1.12,1,.88])
 assert(glassWavelengths[0]>glassWavelengths[1]&&glassWavelengths[1]>glassWavelengths[2])
 assert(dockPrism.bend*Math.max(...glassWavelengths)<dockPrism.padding)
})

test('the composed source is magnified about the lens centre before output strain',()=>{
 const width=100,height=76,left=125,top=-8,zoom=1.13,m=glassSourceMatrix(width,height,left,top,zoom)
 const point=(x,y)=>[m[0]*x+m[2]*y+m[4],m[1]*x+m[3]*y+m[5]]
 const c=point(left+width/2,top+height/2);near(c[0],12+width/2);near(c[1],12+height/2)
 const p=point(left,top);near(p[0],12+(1-zoom)*width/2);near(p[1],12+(1-zoom)*height/2)
})
test('feathered optical masks retain valid cap geometry at narrow and normal sizes',()=>{
 for(const [width,height]of [[2,70],[166.7,72.8],[200,80]]){
  const svg=decodeURIComponent(glassEdgeMask(width,height,.5).slice(5,-2))
  assert(!svg.includes('NaN')&&!svg.includes('Infinity'))
  for(const [,value]of svg.matchAll(/(?:width|height|rx|stroke-width)="([\d.-]+)"/g))assert(Number(value)>=0)
 }
})
