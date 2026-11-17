'use strict'
// Text and layout only. No video frames, fonts or network requests enter this worker.
const simplify=require('./vendor/opencc.cjs').Converter({from:'tw',to:'cn'})
const defaults={enabled:true,region:.5,fontSize:28,fontWeight:400,simplified:true,opacity:.85,duration:8,density:80,scroll:true,top:true,bottom:true,white:false,avoidSubtitles:true,offset:0}
function finite(value,min,max,fallback){const n=Number(value);return Number.isFinite(n)?Math.max(min,Math.min(max,n)):fallback}
function options(value={}){const o={...defaults};for(const k of ['enabled','scroll','top','bottom','white','avoidSubtitles','simplified'])if(typeof value[k]==='boolean')o[k]=value[k]
 for(const [k,min,max]of [['region',0,1],['fontSize',12,96],['fontWeight',100,900],['opacity',.1,1],['duration',8,32],['density',10,160],['offset',-3600,3600]])o[k]=finite(value[k]??o[k],min,max,o[k]);return o}
function decode(text){return text.replace(/&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos);/gi,(_,e)=>{const names={amp:'&',lt:'<',gt:'>',quot:'"',apos:"'"};if(e[0]!=='#')return names[e.toLowerCase()]??'';const n=e[1].toLowerCase()==='x'?parseInt(e.slice(2),16):Number(e.slice(1));return n>0&&n<=0x10ffff&&!(n>=0xd800&&n<=0xdfff)?String.fromCodePoint(n):''})}
function parseXml(xml){if(Buffer.byteLength(xml,'utf8')>64*1024*1024)throw new Error('弹幕文件超过 64 MB，请减少弹幕数量后重试。');if(/<!DOCTYPE|<!ENTITY/i.test(xml))throw new Error('不支持包含外部实体的弹幕文件。')
 const result=[];let seen=0
 for(const match of xml.matchAll(/<d\s+[^>]*\bp\s*=\s*(["'])(.*?)\1[^>]*>([\s\S]*?)<\/d\s*>/gi)){
  if(++seen>200000)throw new Error('弹幕超过 20 万条，请先筛选或更换来源。')
  const p=decode(match[2]).split(','),time=Number(p[0]),mode=Number(p[1]);if(!Number.isFinite(time)||time<0||time>172800||![1,2,3,4,5,6].includes(mode))continue
  const text=decode(match[3].replace(/^<!\[CDATA\[([\s\S]*)\]\]>$/,'$1')).replace(/[\x00-\x1f\x7f]/g,' ').trim().slice(0,180);if(!text)continue
  result.push({time,mode:mode===4?'bottom':mode===5?'top':mode===6?'reverse':'scroll',text,color:Number.isFinite(Number(p[3]))?Math.max(0,Math.min(0xffffff,Number(p[3]))):0xffffff})
 }
 return result.sort((a,b)=>a.time-b.time)
}
function escapeAss(text){return text.replace(/\\/g,'＼').replace(/\{/g,'｛').replace(/\}/g,'｝').replace(/\r?\n/g,' ')}
function textWidth(text,size){let units=0;for(const char of text)units+=char.codePointAt(0)>255?1.12:/[MW@#%&]/.test(char)?1.05:/[A-Z]/.test(char)?.82:char===' '?.4:.68;return Math.ceil(units*size+size*.8)}
function layout(comments,value={},aspect=16/9){const o=options(value),width=1280,height=width/finite(aspect,.4,4,16/9),fontSize=o.fontSize*height/720,line=fontSize*1.5,top=height/45,maxY=height*(o.avoidSubtitles?.83:.98),bottom=Math.min(maxY,height*o.region),lanes=Math.max(0,Math.floor((bottom-top)/line)),fixed=Array(lanes).fill(-Infinity),rolling=Array(lanes).fill(null),reverse=Array(lanes).fill(null),recent=new Map(),items=[]
 let dropped=0,active=[]
 for(const original of comments){const c=o.simplified?{...original,text:simplify(original.text)}:original;if(!o[c.mode==='reverse'?'scroll':c.mode])continue;const repeat=recent.get(c.text);if(repeat!==undefined&&c.time-repeat<.8){dropped++;continue}recent.set(c.text,c.time);if(recent.size>6000)recent.delete(recent.keys().next().value)
  active=active.filter(t=>t>c.time);const duration=c.mode==='top'||c.mode==='bottom'?Math.min(5,o.duration):o.duration;if(active.length>=o.density){dropped++;continue}
  const w=Math.ceil(textWidth(c.text,fontSize)*(1+Math.max(0,o.fontWeight-400)/10000)),v=(width+w)/duration;let lane=-1
  if(c.mode==='top'||c.mode==='bottom'){
   const candidates=Array.from({length:lanes},(_,i)=>c.mode==='bottom'?lanes-1-i:i)
   lane=candidates.find(i=>fixed[i]<=c.time&&(!rolling[i]||rolling[i].time+rolling[i].duration<=c.time)&&(!reverse[i]||reverse[i].time+reverse[i].duration<=c.time))??-1
   if(lane>=0)fixed[lane]=c.time+duration+.1
  }else{
   const occupied=c.mode==='reverse'?reverse:rolling,opposite=c.mode==='reverse'?rolling:reverse
   lane=occupied.findIndex((p,i)=>{if(fixed[i]>c.time||opposite[i]&&opposite[i].time+opposite[i].duration>c.time)return false;if(!p)return true;const dt=c.time-p.time;if(dt>=p.duration)return true;const gap=p.v*dt-p.width-20;return gap>=0&&(v<=p.v||gap>=(v-p.v)*(p.duration-dt))})
   if(lane>=0)occupied[lane]={time:c.time,width:w,v,duration}
  }
  if(lane<0){dropped++;continue}
  const y=top+lane*line
  items.push({t:c.time,d:duration,m:c.mode,w,y,text:escapeAss(c.text),c:o.white?0xffffff:c.color});active.push(c.time+duration)
 }
 return{version:1,width,height,fontSize,fontWeight:o.fontWeight,duration:o.duration,opacity:o.opacity,enabled:o.enabled,offset:o.offset,count:comments.length,displayed:items.length,dropped,items}
}
module.exports={defaults,options,parseXml,layout,escapeAss,textWidth}
