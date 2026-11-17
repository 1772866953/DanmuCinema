'use strict'
// Styled tracks remain with libass. These options never rewrite subtitle files.
const defaults={subtitleOriginal:true,subtitleFont:'sans-serif',subtitleSize:38,subtitleWeight:400,subtitleItalic:false,subtitleColor:'#FFFFFF',subtitleOpacity:1,subtitleOutline:1.65,subtitleOutlineColor:'#000000',subtitleShadow:0,subtitleShadowColor:'#000000'}
function normalize(value={}){
 const result={...defaults}
 for(const key of ['subtitleOriginal','subtitleItalic'])if(typeof value[key]==='boolean')result[key]=value[key]
 if(typeof value.subtitleFont==='string'){const font=value.subtitleFont.replace(/[\x00-\x1f\x7f]/g,'').trim().slice(0,100);if(font)result.subtitleFont=font}
 for(const [key,min,max]of [['subtitleSize',12,96],['subtitleOpacity',0,1],['subtitleOutline',0,8],['subtitleShadow',0,10]]){const n=Number(value[key]);if(value[key]!==undefined&&Number.isFinite(n))result[key]=Math.max(min,Math.min(max,n))}
 result.subtitleWeight=Number(value.subtitleWeight)>=700?700:400
 for(const key of ['subtitleColor','subtitleOutlineColor','subtitleShadowColor'])if(/^#[\da-f]{6}$/i.test(value[key]))result[key]=value[key].toUpperCase()
 return result
}
function properties(value){
 const s=normalize(value),original=s.subtitleOriginal
 const color=rgb=>'#'+Math.round((original?1:s.subtitleOpacity)*255).toString(16).padStart(2,'0').toUpperCase()+rgb.slice(1)
 return {'sub-ass-override':original?'no':'force','secondary-sub-ass-override':original?'no':'force','embeddedfonts':true,'sub-ass':true,'sub-ass-style-overrides':'','sub-ass-scale-with-window':false,'sub-scale':1,'secondary-sub-scale':1,'sub-pos':100,
  'sub-font':original?'sans-serif':s.subtitleFont,'sub-font-size':original?38:s.subtitleSize,'sub-bold':!original&&s.subtitleWeight===700,'sub-italic':!original&&s.subtitleItalic,
  'sub-color':color(original?'#FFFFFF':s.subtitleColor),'sub-outline-size':original?1.65:s.subtitleOutline,'sub-outline-color':color(original?'#000000':s.subtitleOutlineColor),'sub-shadow-offset':original?0:s.subtitleShadow,'sub-back-color':color(original?'#000000':s.subtitleShadowColor)}
}
function args(value){return Object.entries(properties(value)).map(([key,v])=>'--'+key+'='+(typeof v==='boolean'?v?'yes':'no':v))}
module.exports={defaults,normalize,properties,args}
