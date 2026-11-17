'use strict'
const assert=require('node:assert/strict')
function titles(items){return (items||[]).flatMap(i=>[i.title,...titles(i.children)]).filter(Boolean)}
function find(items,label){for(let i=0;i<items.length;i++){if(items[i].title===label)return [i];const sub=items[i].children&&find(items[i].children,label);if(sub)return [i,...sub]}return null}
function navigation(cmd,panel,wait,sleep){return async function choose(label){const p=await panel(),route=find(p.menuTree||[],label);assert(route,'missing '+label);for(let depth=0;depth<route.length;depth++){await cmd('keypress','HOME');await wait(async()=>(await panel()).index===1,'menu home');for(let n=0;n<40&&(await panel()).index!==route[depth]+1;n++){await cmd('keypress','DOWN');await sleep(15)}assert.equal((await panel()).index,route[depth]+1);await cmd('keypress',depth===route.length-1?'ENTER':'RIGHT');if(depth<route.length-1)await wait(async()=>(await panel()).menuDepth===depth+2,'submenu')}await wait(async()=>{const n=await panel();return !n.open||n.id!==p.id},'menu action');await sleep(190)}}
module.exports={titles,navigation}
