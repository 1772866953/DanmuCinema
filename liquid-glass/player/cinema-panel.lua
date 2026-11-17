-- Native ASS controls: one background, vector glyphs, demand-driven animation.
local mp, assdraw, utils = require 'mp', require 'mp.assdraw', require 'mp.utils'
local async_osd=dofile(select(1,require('mp.utils').split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-osd.lua')
local ticker=dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-ticker.lua')
local motion = dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-motion.lua')
local menus=dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-menu.lua')
local M, state, options, hits, drag = {}, nil, {}, {}, nil
local overlay = async_osd.create('ass-events'); overlay.z = 110
local w,h,scale,pixel_x,mouse_x,mouse_y=1280,720,1,1,-1,-1
local bindings,started,ending,previous_dragging,serial={},0,nil,nil,0
local draw,close,finish_close,current_rect,animation,install,invoke_menu
local pressed,clip,locked=nil,nil,false
local feedback_states={}
local qa={qa=false,parent_pid=0};require('mp.options').read_options(qa,'cinema')
local dimensions,ui_weight,properties=nil,400,{}
local hidpi,ui_factor=1,1
local function display_scale(dim)
 -- Retain the chosen size at normal/fullscreen sizes; fit one complete row,
 -- navigation and footer when a high-DPI window becomes unusually small.
 return math.min(hidpi*ui_factor,dim.w/400,dim.h/320)
end
local pending_options={}
local perf={draws=0,total=0,max=0,moves=0,actions=0}
local clamp=function(v,a,b)return math.max(a,math.min(b,v))end
local function esc(s)return tostring(s or ''):gsub('\\','＼'):gsub('{','｛'):gsub('}','｝'):gsub('[\r\n]',' ')end
local function clipping(a)if clip then a:append(string.format('{\\clip(%d,%d,%d,%d)}',clip.x,clip.y,clip.x+clip.w,clip.y+clip.h))end end
local function box(a,x,y,bw,bh,r,colour,alpha)
 a:new_event();a:pos(0,0);a:an(7);clipping(a)
 a:append(string.format('{\\bord0\\shad0\\1c&H%s&\\1a&H%02X&}',colour,clamp(math.floor(alpha or 0),0,255)))
 a:draw_start();a:round_rect_cw(x,y,x+bw,y+bh,r);a:draw_stop()
end
local function text(a,x,y,label,size,colour,align,alpha)
 -- Pixel-aligned positions; never scale, blur, or crossfade overlapping glyphs.
 a:new_event();a:pos(math.floor(x/pixel_x+.5)*pixel_x,math.floor(y/scale+.5)*scale);a:an(align or 7);clipping(a)
 local pixel_size=math.floor(size/scale+.5)*scale
 a:append(string.format('{\\fnDanmuCinema UI\\fs%.6f\\b%d\\bord0\\shad0\\blur0\\be0\\1c&H%s&\\alpha&H%02X&}%s',pixel_size,ui_weight,colour or 'F1F6F4',clamp(math.floor(alpha or 0),0,255),esc(label)))
end
local function inside(v)return mouse_x>=v.x and mouse_x<=v.x+v.w and mouse_y>=v.y and mouse_y<=v.y+v.h end
local function hit(x,y,bw,bh,callback,slider,key)
 if ending then return end
 if clip then local bottom,right=math.min(y+bh,clip.y+clip.h),math.min(x+bw,clip.x+clip.w);x=math.max(x,clip.x);y=math.max(y,clip.y);bw=right-x;bh=bottom-y end
 if bw>0 and bh>0 then hits[#hits+1]={x=x,y=y,w=bw,h=bh,callback=callback,slider=slider,key=key}end
end
local function fit(s,width,size)
 local full=0;for c in tostring(s):gmatch('[%z\1-\127\194-\244][\128-\191]*')do full=full+size*(#c>1 and 1.08 or c:match('[MW@]')and 1 or .68)end
 if full<=width then return tostring(s)end
 local chars,total={},0
 for c in tostring(s):gmatch('[%z\1-\127\194-\244][\128-\191]*')do
  total=total+size*(#c>1 and 1.08 or c:match('[MW@]')and 1 or .68)
  if total>width-size*1.2 then return table.concat(chars)..'…'end;chars[#chars+1]=c
 end;return table.concat(chars)
end
local function feedback(a,key,x,y,bw,bh,alpha)
 local hover=inside({x=x,y=y,w=bw,h=bh})and (not clip or inside(clip)) and not locked
 local amount=motion.get('hover-'..serial..key,hover and 1 or 0,.14,0)
 local down=motion.get('press-'..serial..key,pressed==key and 1 or 0,.09,0)
 feedback_states[#feedback_states+1]={key=key,hover=amount,press=down}
 if amount>.001 then box(a,x+down,y+down,bw-2*down,bh-2*down,(key=='close'or key=='menu-close')and math.min(bw,bh)/2 or 9,'D9F0DD',255-(255-alpha)*(.10*amount+.09*down))end
end
local function button(a,key,label,x,y,bw,bh,alpha,callback,selected)
 local common=state and state.tab~='danmu'and state.tab~='sync';local size=16
 box(a,x,y,bw,bh,common and 8 or 10,selected and 'B8DDB3' or common and '52675C' or '41544B',alpha)
 feedback(a,key,x,y,bw,bh,alpha);text(a,x+bw/2,y+bh/2,fit(label,bw-12,size),size,selected and '162A23' or 'E9F2ED',5,alpha)
 hit(x,y,bw,bh,callback,nil,key)
end
local function send(key,v)options[key]=v;if key=='subtitleOriginal'and state then state.rowFirst=1;motion.reset('scroll-')end;pending_options[key]={value=v,at=mp.get_time()};mp.commandv('script-message','cinema-option',key,tostring(v))end
local rows={
 danmu={
  {key='enabled',label='显示弹幕',kind='toggle'},{key='simplified',label='繁体转简体',kind='toggle'},
  {key='fontSize',label='字号',min=12,max=96,step=.1,default=28},{key='fontWeight',label='字重',min=100,max=900,step=10,default=400},
  {key='duration',label='滚动时长',min=8,max=32,step=.1,unit=' 秒',default=8},{key='opacity',label='不透明度',min=.1,max=1,step=.01,percent=true,default=.85},
  {key='region',label='显示区域',min=0,max=1,step=.01,percent=true,default=.5},{key='density',label='同屏上限',min=10,max=160,step=1,unit=' 条',default=80},
  {key='danmuFps',label='弹幕帧率',kind='choice',values={60,120,0},labels={'60','120','跟随显示器'}},
  {key='scroll',label='滚动弹幕',kind='toggle'},{key='top',label='顶部固定',kind='toggle'},
  {key='bottom',label='底部固定',kind='toggle'},{key='avoidSubtitles',label='避让字幕',kind='toggle'},{key='white',label='统一白色',kind='toggle'},
 },
 sync={
  {key='audioDelay',property='audio-delay',label='音频偏移',min=-30,max=30,step=.05,unit=' 秒',default=0},
  {key='subtitleDelay',property='sub-delay',label='字幕偏移',min=-30,max=30,step=.05,unit=' 秒',default=0},
  {key='offset',label='弹幕偏移',min=-60,max=60,step=.1,unit=' 秒',default=0},
 },
 play={
  {key='speed',property='speed',label='播放速度',min=.5,max=3,step=.05,unit=' 倍',default=1},
  {key='preloadBudget',label='播放预读预算',min=0,max=100,step=1,unit='%',default=25},
  {key='holdSpeed',label='长按倍速',min=2,max=6,step=1,unit=' 倍',default=2},
  {key='volume',property='volume',label='音量',min=0,max=100,step=1,default=70},
  {key='repeatMode',label='循环模式',kind='choice',values={'no','file','list'},labels={'关闭','单集','列表'}},
  {key='aspect',property='video-aspect-override',label='画面比例',kind='choice',values={'no',16/9,4/3},labels={'原始','16:9','4:3'},default='no'},
  {key='rotate',property='video-rotate',label='画面旋转',kind='choice',values={0,90,180,270},labels={'0°','90°','180°','270°'}},
  {key='picture',label='画面调整',kind='action',actions={{'恢复原始画面','reset'}}},
  {key='frames',label='逐帧播放（自动暂停）',kind='action',actions={{'上一帧','back'},{'下一帧','forward'}}},
  {key='ab',label='A–B 片段循环',kind='action',actions={{'标记 A','a'},{'标记 B','b'},{'清除','clear'}}},
  {key='mute',property='mute',label='静音',kind='toggle'},
  {key='subVisible',property='sub-visibility',label='显示字幕',kind='toggle'},
 }
}
local old_play=rows.play;local by_key={};for _,r in ipairs(old_play)do by_key[r.key]=r end
rows.play={by_key.speed,by_key.holdSpeed,by_key.preloadBudget,by_key.repeatMode,by_key.frames,by_key.ab}
rows.video={by_key.aspect,by_key.rotate,by_key.picture}
rows.audio={by_key.volume,by_key.mute}
rows.subtitle={by_key.subVisible,{key='subtitleOriginal',label='保留字幕原样式',kind='toggle'},
 {key='subtitleFont',label='字体',kind='choice',custom=true,values={'sans-serif','Noto Sans SC','Microsoft YaHei'},labels={'系统默认','思源黑体','微软雅黑'}},
 {key='subtitleSize',label='字号',min=12,max=96,step=.5,default=38,custom=true},
 {key='subtitleWeight',label='字重',kind='choice',values={400,700},labels={'常规','粗体'},custom=true},
 {key='subtitleItalic',label='斜体',kind='toggle',custom=true},
 {key='subtitleColor',label='文字颜色',kind='choice',values={'#FFFFFF','#FFE08A','#8FE3FF','#A8E6A1','#FFABD5'},labels={'白','黄','青','绿','粉'},custom=true},
 {key='subtitleOpacity',label='不透明度',min=0,max=1,step=.01,percent=true,default=1,custom=true},
 {key='subtitleOutline',label='描边宽度',min=0,max=8,step=.05,default=1.65,custom=true},
 {key='subtitleOutlineColor',label='描边颜色',kind='choice',values={'#000000','#FFFFFF','#26352D'},labels={'黑','白','深绿'},custom=true},
 {key='subtitleShadow',label='阴影距离',min=0,max=10,step=.1,default=0,custom=true},
 {key='subtitleShadowColor',label='阴影颜色',kind='choice',values={'#000000','#FFFFFF','#26352D'},labels={'黑','白','深绿'},custom=true}}
local function value(r)
 if r.key=='repeatMode'then local file,list=properties['loop-file'],properties['loop-playlist'];return file and file~='no'and 'file' or list and list~='no'and 'list' or 'no' end
 if r.property then local v=properties[r.property];if r.key=='aspect'and (v==nil or type(v)=='number'and v<0)then return 'no'end;if v~=nil then return v end;if r.kind=='toggle'then return false end;return r.default or 0 end
 return options[r.key]
end
local function set(r,v,commit)
 if r.kind~='toggle' and r.kind~='choice' then v=math.floor(clamp(v,r.min,r.max)/r.step+.5)*r.step end
 if r.key=='repeatMode' then properties['loop-file']=v=='file'and 'inf'or false;properties['loop-playlist']=v=='list'and 'inf'or false;mp.set_property('loop-file',v=='file' and 'inf' or 'no');mp.set_property('loop-playlist',v=='list' and 'inf' or 'no')
 elseif r.property then properties[r.property]=v;mp.set_property_native(r.property,v);if commit and (r.key=='audioDelay'or r.key=='subtitleDelay'or r.key=='volume')then send(r.key,v)end
 else options[r.key]=v;if commit then send(r.key,v)end end
end
local function slide(r,p,width)
 local v=clamp(r.min+(r.max-r.min)*p,r.min,r.max)
 local radius=math.min((r.max-r.min)*.018,math.max(r.step*2,(r.max-r.min)*scale*2/(width or 300)))
 if r.min<0 then radius=math.min(radius,.15)end
 if r.default and math.abs(v-r.default)<=radius then v=r.default end
 return v
end
local function act(key)
 if key=='back'or key=='forward'then mp.commandv('no-osd',key=='back' and 'frame-back-step' or 'frame-step');return end
 if key=='a'then mp.set_property_number('ab-loop-a',mp.get_property_number('time-pos',0));mp.set_property('ab-loop-b','no');state.notice='已标记 A 点'
 elseif key=='b'then
  local start=mp.get_property_number('ab-loop-a');local pos=mp.get_property_number('time-pos',0)
  if not start or pos<=start+.01 then state.notice='请先标记 A，再在较后的时间标记 B'else mp.set_property_number('ab-loop-b',pos);state.notice=string.format('循环 %.1f–%.1f 秒',start,pos)end
 elseif key=='clear'then mp.set_property('ab-loop-a','no');mp.set_property('ab-loop-b','no');state.notice='已清除片段循环'
 elseif key=='reset'then mp.set_property('video-aspect-override','no');for k,v in pairs({['video-rotate']=0,['video-zoom']=0,['video-pan-x']=0,['video-pan-y']=0})do mp.set_property_number(k,v)end;state.notice='已恢复原始画面'
 end
 if state and state.menu and state.notice then mp.commandv('script-message-to','cinema_input','cinema-toast',key=='reset'and '画面'or '片段循环',key=='a'and '已标记 A'or key=='b'and (mp.get_property('ab-loop-b')~='no'and '已标记 B'or '请先标记 A')or key=='clear'and '已清除'or '已恢复')end
end
local function row(a,r,x,y,bw,alpha)
 if r.custom and options.subtitleOriginal~=false then return end
 local common=state.tab~='danmu'and state.tab~='sync';local rh=common and 62 or 48;local top=common and 10 or 7;local size=16
 local v=value(r);if v==nil then v=r.kind=='toggle'or r.default or r.min or 60 end
 box(a,x,y,bw,rh,12,common and '35483E' or '343F3B',math.min(255,alpha+(common and 12 or 35)));text(a,x+15,y+(common and r.kind=='toggle'and 23 or top),r.label,size,'CFDDD7',7,alpha)
 local key=r.key
 if r.kind=='toggle'then
  feedback(a,key,x,y,bw,rh,alpha)
  local t=motion.get('toggle-'..serial..key,v and 1 or 0,.16)
  box(a,x+bw-54,y+(rh-22)/2,38,22,11,v and 'A8CBA4'or '60716B',alpha)
  box(a,x+bw-49+16*t,y+(rh-16)/2,16,16,8,'F0F7F3',alpha)
  hit(x,y,bw,rh,function()set(r,not value(r),true)end,nil,key)
 elseif r.kind=='choice'or r.kind=='action'then
  -- Put choices on their own lower line so narrow windows never overlap labels.
  local values=r.values or r.actions;local count=#values;local gap=common and 4 or 6;local cell=common and (bw-28-(count-1)*gap)/count or math.min(r.kind=='action'and 150 or 110,(bw-26-(count-1)*6)/count);local left=common and x+14 or x+bw-13-count*cell-(count-1)*6
  for i,n in ipairs(values)do local bx=left+(i-1)*(cell+gap);local label=r.labels and r.labels[i]or r.actions and n[1]or tostring(n)
   button(a,key..i,label,bx,y+(common and 32 or 24),cell,common and 26 or 22,alpha,function()if r.actions then act(n[2])else set(r,n,true)end end,r.values and v==n)
  end
 else
  local label=r.percent and string.format('%.0f%%',v*100)or string.format(r.step<.1 and '%.2f%s'or r.step<1 and '%.1f%s'or '%.0f%s',v,r.unit or '')
  text(a,x+bw-15,y+top,label,size,'F1F6F4',9,alpha)
  hit(x+bw-96,y,88,24,function()set(r,r.default,true)end,nil,key..'-reset')
  local sx,sw=x+16,bw-32;local sy=common and 46 or 34;local q=(clamp(v,r.min,r.max)-r.min)/(r.max-r.min);local d=(r.default-r.min)/(r.max-r.min)
  box(a,sx,y+sy,sw,3,1.5,'71847B',alpha);if q>0 then box(a,sx,y+sy,math.max(3,sw*q),3,1.5,'B3DAB1',alpha)end
  -- Persistent default detent; independent of the current thumb position.
  box(a,sx+sw*d-2,y+sy-5,4,13,2,'C9D8C5',math.min(255,alpha+24))
  local hot=motion.get('thumb-'..serial..key,(drag and drag.row.key==key)and 1 or 0,.1,0)
  box(a,sx+sw*q-5-hot,y+sy-4-hot,10+2*hot,10+2*hot,5+hot,'DCEDE4',alpha)
  hit(sx-7,y+sy-10,sw+14,24,nil,{row=r,left=sx,width=sw},key)
 end
end
local function refresh_items()
 local source=state.source_items or {};if state.get_items then local items,selected=state.get_items();source=items;if not state.loaded then state.index=selected or state.index;state.loaded=true end end
 state.items={};state.mapping={};local query=(state.query or ''):lower()
 for i,label in ipairs(source)do if query==''or label:lower():find(query,1,true)then state.items[#state.items+1]=label;state.mapping[#state.items]=i end end
 state.index=clamp(state.index or 1,1,math.max(1,#state.items));state.first=clamp(state.first or 1,1,math.max(1,#state.items-(state.listRows or 9)+1))
 if state.index<state.first then state.first=state.index elseif state.index>=state.first+(state.listRows or 9) then state.first=state.index-(state.listRows or 9)+1 end
end
local function wake()if animation and state and not animation:is_enabled()then animation:resume()end end
draw=function()
 if not state then overlay:park();return end
 local draw_started=mp.get_time()
 local dim=dimensions;if not dim or dim.h==0 then return end
 local factor=display_scale(dim);h=math.floor(dim.h/factor+.5);w=math.floor(dim.w/factor+.5);scale=h/dim.h;pixel_x=w/dim.w
 local age=mp.get_time()-started;local f=ending and clamp(1-(mp.get_time()-ending)/.14,0,1)or clamp(age/.16,0,1);local alpha=math.floor((1-f)*255)
 if ending and f==0 then finish_close();return end
 local content=1
 if state.switchAt then
  local t=mp.get_time()-state.switchAt
  if t<.075 then content=1-t/.075
  else if state.pendingTab then state.tab=state.pendingTab;state.pendingTab=nil;state.rowFirst=1;motion.reset('scroll-')end;content=clamp((t-.075)/.13,0,1)end
  if t>=.205 then state.switchAt=nil end
 end
 locked=ending~=nil
 local ca=math.floor(255-(255-alpha)*content)
 local a=assdraw.ass_new();hits={};clip=nil;feedback_states={}
 local bw=math.min(760,w-36);local targetHeight=state.tab and (state.tab=='sync'and 390 or state.tab=='danmu'and 614 or 390)or state.panelHeight or 552
 local bh=math.min(h-24,motion.get('height-'..serial,targetHeight,.18));local x,y=(w-bw)/2,(h-bh)/2
 current_rect={x=x,y=y,w=bw,h=bh}
 if not state.menu then
 box(a,x,y,bw,bh,22,'202D27',math.min(255,alpha+14))
 text(a,x+25,y+19,fit(state.tab and '播放设置'or(state.prompt or '播放菜单'):gsub('：$',''),bw-92,22),22,nil,7,alpha)
 feedback(a,'close',x+bw-48,y+12,32,32,alpha);text(a,x+bw-32,y+28,'×',18,'CCDCD4',5,alpha);hit(x+bw-48,y+12,32,32,function()close()end,nil,'close')
 end
 if state.menu then
  current_rect=menus.draw(a,state.menu,{w=w,h=h,alpha=alpha,wake=wake,close=function()close()end,invoke=function(item)invoke_menu(item)end,api={box=box,text=text,feedback=feedback,hit=hit}});x,y,bw,bh=current_rect.x,current_rect.y,current_rect.w,current_rect.h
 elseif state.tab then
  local tabs={{'danmu','弹幕'},{'play','播放'},{'video','画面'},{'audio','声音'},{'subtitle','字幕'},{'sync','同步'}};local tabColumns=bw<450 and 3 or 6;local tw=(bw-48-(tabColumns-1)*6)/tabColumns;local compact=bh<340;local tabY=compact and 48 or 63;local contentTop=tabY+(tabColumns==3 and 76 or 38)+7;state.contentTop=contentTop
  -- Tabs remain interactive while content fades, allowing a new target without stacking panels.
  local hold=locked;locked=ending~=nil
  for i,t in ipairs(tabs)do button(a,'tab-'..t[1],t[2],x+24+((i-1)%tabColumns)*(tw+6),y+tabY+math.floor((i-1)/tabColumns)*38,tw,32,alpha,function()if state.tab==t[1]then state.pendingTab=nil;state.switchAt=nil else state.pendingTab=t[1];state.switchAt=mp.get_time()end;wake()end,state.tab==t[1])end;locked=hold
  local columns=state.tab~='sync'and bw>=650 and 2 or 1;local lineHeight=state.tab~='danmu'and state.tab~='sync'and 70 or 56;local cw=(bw-48-(columns-1)*12)/columns
  state.visibleRows=math.max(1,math.floor((bh-contentTop-52)/lineHeight))*columns
  local visibleSettings={};for _,r in ipairs(rows[state.tab])do if not r.custom or options.subtitleOriginal==false then visibleSettings[#visibleSettings+1]=r end end
  local totalLines=math.ceil(#visibleSettings/columns);local visibleLines=math.max(1,math.floor((bh-contentTop-52)/lineHeight))
  state.rowFirst=clamp(state.rowFirst or 1,1,math.max(1,totalLines-visibleLines+1))
  local scroll=motion.get('scroll-'..serial..state.tab,state.rowFirst-1,.18);state.visualScroll=scroll
  clip={x=math.ceil(x+23),y=math.ceil(y+contentTop),w=math.floor(bw-46),h=visibleLines*lineHeight}
  for line=math.floor(scroll),math.min(totalLines-1,math.ceil(scroll)+visibleLines-1)do
   for col=0,columns-1 do local r=visibleSettings[line*columns+col+1];if r then row(a,r,x+24+col*(cw+12),y+contentTop+1+(line-scroll)*lineHeight,cw,ca)end end
  end
  clip=nil
  if state.tab=='sync'then
   if not compact then text(a,x+26,y+bh-96,fit('正值延后，负值提前；按影片保存。',bw-52,14),14,'B4C9BD',7,ca)end
   button(a,'reset-sync','重置全部偏移',x+24,y+bh-(compact and 41 or 65),150,compact and 28 or 34,ca,function()for _,r in ipairs(rows.sync)do set(r,0,true)end end)
  else text(a,x+26,y+bh-(compact and 22 or 40),fit(state.notice or (state.tab=='subtitle'and (options.subtitleOriginal~=false and '使用字幕原有样式和内嵌字体。'or '图形字幕不支持修改字体。')or ''),bw-52,13),13,'B4C9BD',7,ca)end
 else
  state.listRows=math.max(1,math.min(9,math.floor((bh-110)/47)));refresh_items();text(a,x+25,y+54,fit(state.query~=''and '筛选：'..state.query or string.format('%d 项  ·  键入名称筛选 / 滚轮翻页 / Enter 确认',#state.items),bw-50,13),13,'B4C9BD',7,alpha)
  local scroll=motion.get('scroll-'..serial,state.first-1,.18);state.visualScroll=scroll
  clip={x=math.ceil(x+18),y=math.ceil(y+88),w=math.floor(bw-36),h=math.min(state.listRows,#state.items)*47}
  for j=math.floor(scroll),math.ceil(scroll)+state.listRows-1 do local i=j+1;local label=state.items[i];if label then local ry=y+89+(j-scroll)*47;local selected=i==state.index
   box(a,x+18,ry,bw-36,42,11,selected and 'A4C6A2'or '34473C',selected and alpha or math.min(255,alpha+90))
   feedback(a,'item-'..i,x+18,ry,bw-36,42,alpha);text(a,x+34,ry+21,fit(label,bw-68,16),16,selected and '15291F'or 'EAF1ED',4,alpha)
   hit(x+18,ry,bw-36,42,function()local old=state;local chosen=i;if not old.keep_open then close(true)end;old.submit(old.mapping[chosen]or chosen);if state==old then old.index=chosen end end,nil,'item-'..i)
  end end;clip=nil
 end
 if overlay.hidden or overlay.data~=a.text or overlay.res_x~=w or overlay.res_y~=h then overlay.res_x,overlay.res_y,overlay.data=w,h,a.text;overlay:update()end
 if qa.qa then
 local menu_panes={};if state.menu then for _,p in ipairs(state.menu.panes)do menu_panes[#menu_panes+1]={x=p.x,y=p.y,w=p.w,h=p.h,depth=p.depth}end end
 if state.menu then state.items={};for _,item in ipairs(menus.items_at(state.menu,state.menu.depth)or{})do state.items[#state.items+1]=item.title or '—'end;state.index=state.menu.selection[state.menu.depth]or 1 end
 local controls={};for _,v in ipairs(hits)do controls[#controls+1]={x=v.x,y=v.y,w=v.w,h=v.h,key=v.key,value=v.slider and value(v.slider.row),default=v.slider and v.slider.row.default}end
 mp.set_property_native('user-data/cinema-panel',{id=serial,open=not ending,fading=ending~=nil,opacity=f,contentOpacity=content,tab=state.tab or (state.menu and 'menu'or 'list'),pixelScale=1/scale,bodyFontPixels=16/scale,listRows=state.listRows,menuTree=state.menu and state.menu.items,menuDepth=state.menu and state.menu.depth,menuPanes=menu_panes,count=state.items and #state.items or 0,index=state.index,first=state.first,rowFirst=state.rowFirst,scroll=state.visualScroll,items=state.items,controls=controls,feedback=feedback_states,pressed=pressed,animating=motion.active(),uiFontWeight=ui_weight,uiFontSize=16*ui_factor,notice=state.notice,rect=current_rect})
 end
 local elapsed=(mp.get_time()-draw_started)*1000;perf.draws=perf.draws+1;perf.total=perf.total+elapsed;perf.max=math.max(perf.max,elapsed)
 if motion.active()or state.switchAt then wake()end
end
animation=ticker.create(function()
 draw()
 if not state then animation:kill()
 elseif not ending and not state.switchAt and mp.get_time()-started>.18 and not motion.active()and not(state.menu and state.menu.animating)then
  -- A transition can finish between draw() and the clock check. Commit its
  -- exact final geometry/opacity before sleeping instead of leaving a near-end
  -- frame (and stale active state) on screen until the next pointer movement.
  draw();animation:kill()
 end
end,'settings');animation:kill()
local function bind(key,name,fn,flags)name='cinema-panel-'..name;bindings[#bindings+1]=name;mp.add_forced_key_binding(key,name,fn,flags)end
finish_close=function()
 if state and state.menu then menus.cancel_hover(state.menu)end
 for _,name in ipairs(bindings)do mp.remove_key_binding(name)end;bindings={}
 if previous_dragging then for key,v in pairs(previous_dragging)do mp.set_property_native(key,v)end;previous_dragging=nil end
 mp.commandv('script-message-to','modernz','cinema-panel','no')
 state=nil;ending=nil;pressed=nil;animation:kill();overlay:park();mp.set_property_native('user-data/cinema-panel',{open=false})
end
close=function(immediate)
 if not state or ending and not immediate then return end
 if drag then set(drag.row,value(drag.row),true);drag=nil end
 if immediate then finish_close()else state.pendingTab=nil;state.switchAt=nil;ending=mp.get_time();mp.set_property_bool('user-data/cinema-panel/open',false);wake()end
end
local function search_bindings()
 -- Installed even on settings so a subsequent list can reuse the same input owner.
 bind('any_unicode','search',function(e)if state and not state.tab and(e.event=='down'or e.event=='press')and e.key_text then state.query=(state.query or '')..e.key_text;state.index=1;state.first=1;wake()end end,{complex=true})
 bind('BS','backspace',function()if state and not state.tab then state.query=(state.query or ''):gsub('[%z\1-\127\194-\244][\128-\191]*$','');state.index=1;state.first=1;wake()end end,{repeatable=true})
end
install=function()
 if #bindings>0 then return end
 previous_dragging={}
 for _,key in ipairs({'input-builtin-dragging','window-dragging'})do local old=mp.get_property_native(key);if type(old)=='boolean'then previous_dragging[key]=old;mp.set_property_bool(key,false)end end
 mp.commandv('script-message-to','modernz','cinema-panel','yes')
 bind('ESC','close',function()if state and state.menu and menus.left(state.menu)then wake()else close()end end);bind('MBTN_RIGHT','right',function()close()end)
 bind('MOUSE_MOVE','move',function()perf.moves=perf.moves+1;
  if ending then return end;local x,y=mp.get_mouse_pos();mouse_x=x*scale;mouse_y=y*scale
  if state and state.menu then menus.move(state.menu,mouse_x,mouse_y,wake)end
  if drag then set(drag.row,slide(drag.row,(mouse_x-drag.left)/drag.width,drag.width),false)end;wake()
 end)
 local synthetic_double=-1
 local function pointer_click(e,is_double)
  -- Both physical presses already arrive through MBTN_LEFT. The extra double
  -- gesture can be delayed until an animated layout has moved under the cursor;
  -- treating it as a click would activate a different control or end a drag.
  if is_double then synthetic_double=mp.get_time();return end
  if ending then return end
  if e.event=='up'then
   if e.canceled and drag and mp.get_time()-synthetic_double<.05 then return end
   if drag then set(drag.row,value(drag.row),true);drag=nil end;pressed=nil;wake();return
  end
  if e.event~='down'and e.event~='press'then return end
  local x,y=mp.get_mouse_pos();mouse_x=x*scale;mouse_y=y*scale
  for hi=#hits,1,-1 do local v=hits[hi];if inside(v)then pressed=v.key;perf.actions=perf.actions+1
   if v.slider then drag=v.slider;set(drag.row,slide(drag.row,(mouse_x-drag.left)/drag.width,drag.width),e.event=='press');if e.event=='press'then drag=nil end else v.callback()end
   if e.event=='press'then mp.add_timeout(.08,function()pressed=nil;if state then wake()end end)end
   wake();return
  end end
  if state.menu then if not menus.contains(state.menu,mouse_x,mouse_y)then close()end elseif current_rect and not inside(current_rect)then close()end
 end
 bind('MBTN_LEFT','click',function(e)pointer_click(e,false)end,{complex=true})
 bind('MBTN_LEFT_DBL','double',function(e)pointer_click(e,true)end,{complex=true})
 local function move(n)
  if not state or ending then return end
  if state.menu then menus.navigate(state.menu,n);wake();return end
  if state.pendingTab then state.tab=state.pendingTab;state.pendingTab=nil;state.switchAt=mp.get_time()-.075;state.rowFirst=1;motion.reset('scroll-')end
  if state.tab then state.rowFirst=clamp((state.rowFirst or 1)+n,1,math.max(1,math.ceil(#rows[state.tab]/(state.tab~='sync'and current_rect.w>=650 and 2 or 1))-math.floor((current_rect.h-(state.contentTop or 108)-52)/(state.tab~='danmu'and state.tab~='sync'and 70 or 56))+1))
  else state.index=clamp(state.index+n,1,math.max(1,#state.items))end;wake()
 end
 bind('UP','up',function()move(-1)end,{repeatable=true});bind('DOWN','down',function()move(1)end,{repeatable=true})
 bind('HOME','home',function()move(-100000)end);bind('END','end',function()move(100000)end)
 bind('WHEEL_UP','wheel-up',function()move(-3)end,{repeatable=true});bind('WHEEL_DOWN','wheel-down',function()move(3)end,{repeatable=true})
 bind('ENTER','submit',function()if state and state.menu then local item=menus.current(state.menu);if item then if not menus.right(state.menu)then invoke_menu(item)end;wake()end elseif state and not state.tab and #state.items>0 then local old=state;local i=old.index;if not old.keep_open then close(true)end;old.submit(old.mapping[i]or i);wake()end end)
 bind('PGUP','page-up',function()move(-9)end,{repeatable=true});bind('PGDWN','page-down',function()move(9)end,{repeatable=true})
end
local function clear_menu_keys()mp.remove_key_binding('cinema-panel-menu-right');mp.remove_key_binding('cinema-panel-menu-left')end
local function abandon_menu()if state and state.menu then menus.cancel_hover(state.menu)end end
function M.select(v)clear_menu_keys();abandon_menu()
 serial=serial+1;motion.reset('');state=v;state.source_items=v.items;state.panelHeight=117+47*math.max(3,math.min(9,#(v.items or {})));state.query='';state.index=v.default_item or 1;state.first=1;ending=nil;pressed=nil;started=mp.get_time();install();search_bindings();mp.set_property_bool('user-data/cinema-panel/open',true);draw();wake()
end
function M.settings(tab)clear_menu_keys();abandon_menu()
 serial=serial+1;motion.reset('');state={tab=rows[tab]and tab or 'danmu'};ending=nil;pressed=nil;started=mp.get_time();install();mp.remove_key_binding('cinema-panel-search');mp.remove_key_binding('cinema-panel-backspace');mp.set_property_bool('user-data/cinema-panel/open',true);draw();wake()
end
mp.register_script_message('cinema-panel-diagnostics',function(value)qa.qa=value=='yes';wake()end)
mp.register_script_message('cinema-panel-perf',function(reset)perf.ticker=animation:stats();perf.overlay=overlay:stats();mp.set_property_native('user-data/cinema-panel-perf',perf);if reset=='reset'then perf={draws=0,total=0,max=0,moves=0,actions=0}end end)
invoke_menu=function(item)
 if item.disabled then return end
 if item.action then close();act(item.action);wake();return end
 if not item.replace then close()end
 if item.command then mp.command(item.command)end
end
function M.menu(items)
 clear_menu_keys();abandon_menu();serial=serial+1;motion.reset('');local dim=dimensions or {w=1280,h=720};local mx,my=mp.get_mouse_pos()
 state={menu=menus.new(items,math.max(8,mx/display_scale(dim)),math.max(8,my/display_scale(dim)))};ending=nil;pressed=nil;started=mp.get_time();install()
 mp.remove_key_binding('cinema-panel-search');mp.remove_key_binding('cinema-panel-backspace')
 bind('RIGHT','menu-right',function()if state and state.menu then menus.right(state.menu);wake()end end)
 bind('LEFT','menu-left',function()if state and state.menu then if not menus.left(state.menu)then close()else wake()end end end)
 mp.set_property_bool('user-data/cinema-panel/open',true);draw();wake()
end
M.terminate=close
mp.register_script_message('cinema-settings',M.settings)
mp.register_script_message('cinema-state',function(json)local v=utils.parse_json(json);if v then for k,n in pairs(v)do if not drag or drag.row.key~=k then local pending=pending_options[k];local equal=pending and (pending.value==n or type(n)=='number'and type(pending.value)=='number'and math.abs(n-pending.value)<.000001);if not pending or equal or mp.get_time()-pending.at>2 then options[k]=n;pending_options[k]=nil end end end;if state then wake()end end end)
for _,property in ipairs({'speed','volume','mute','sub-visibility','video-aspect-override','video-rotate','loop-file','loop-playlist','audio-delay','sub-delay'})do mp.observe_property(property,'native',function(_,v)properties[property]=v;if state and state.tab then wake()end end)end
mp.observe_property('playlist','native',function()if state and state.get_items then wake()end end)
mp.observe_property('osd-dimensions','native',function(_,v)dimensions=v;if state then draw();wake()end end)
mp.observe_property('display-hidpi-scale','number',function(_,v)hidpi=v and v>0 and v or 1;if state then wake()end end)
mp.observe_property('user-data/cinema-ui-size','number',function(_,v)ui_factor=(v or 16)/16;if state then wake()end end)
mp.observe_property('user-data/cinema-ui-weight','number',function(_,v)ui_weight=v or 400;if state then wake()end end)
mp.observe_property('focused','bool',function(_,v)if v==false then if drag then set(drag.row,value(drag.row),true);drag=nil end;pressed=nil;if state then wake()end end end)
mp.register_event('start-file',function()mp.set_property('ab-loop-a','no');mp.set_property('ab-loop-b','no')end)
mp.register_event('shutdown',function()animation:kill();overlay:remove()end)
overlay:prime('panel')
return M
