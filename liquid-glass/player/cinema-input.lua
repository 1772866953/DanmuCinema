local mp, assdraw = require 'mp', require 'mp.assdraw'
local async_osd=dofile(select(1,require('mp.utils').split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-osd.lua')
local ticker=dofile(select(1,require('mp.utils').split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-ticker.lua')
local overlay=async_osd.create('ass-events');overlay.z=70
local hint, expires, hold, timer, draw_timer, last_volume, ready_at = nil,0,nil,nil,nil,nil,math.huge
local function rounded(a,x,y,w,h,r,color,alpha)
 a:new_event();a:pos(0,0);a:an(7);a:append(string.format('{\\bord0\\shad0\\1c&H%s&\\alpha&H%02X&}',color,alpha));a:draw_start();a:round_rect_cw(x,y,x+w,y+h,r);a:draw_stop()
end
local dimensions,ui_weight,panel_open,ui_factor,hidpi=nil,400,false,1,1
local function label(a,x,y,s,size,alpha,align)
 a:new_event();a:pos(x,y);a:an(align or 7);a:append(string.format('{\\fnDanmuCinema UI\\fs%d\\b%d\\bord0\\shad0\\1c&HE9F4ED&\\alpha&H%02X&}%s',size,ui_weight,alpha,s))
end
local function time(t)t=math.max(0,math.floor(t));return string.format('%02d:%02d',math.floor(t/60),t%60)end
local function draw()
 local now=mp.get_time();if not hint or now>=expires then overlay:park();draw_timer:kill();hint=nil;mp.set_property_native('user-data/cinema-hint', {visible=false});return end
 if panel_open then overlay:park();return end
 local dim=dimensions;if not dim or dim.h==0 then return end
 local h=dim.h/hidpi/ui_factor;local w=dim.w/dim.h*h;local bw=hint.ratio and 292 or 196;local bh=hint.ratio and 66 or 46;local x,y=(w-bw)/2,math.max(12,h-148);local a=assdraw.ass_new();local alpha=math.floor(math.max(0,1-math.min(1,(expires-now)/.13))*255)
 rounded(a,x,y,bw,bh,16,'1D3028',math.min(255,alpha+32))
 label(a,x+18,y+12,hint.label,16,alpha);label(a,x+bw-18,y+12,hint.value,16,alpha,9)
 if hint.ratio then rounded(a,x+18,y+46,256,3,1.5,'6D8278',alpha);rounded(a,x+18,y+46,math.max(3,256*hint.ratio),3,1.5,'B0D8B4',alpha)end
 if overlay.hidden or overlay.data~=a.text or overlay.res_x~=w or overlay.res_y~=h then overlay.res_x,overlay.res_y,overlay.data=w,h,a.text;overlay:update()end
 if not hint.published then mp.set_property_native('user-data/cinema-hint',{visible=true,label=hint.label,value=hint.value,bar=hint.ratio~=nil,width=bw,height=bh});hint.published=true end
end
draw_timer=ticker.create(draw,'hint');draw_timer:kill()
local function show(label,value,ratio,duration)hint={label=label,value=value,ratio=ratio};expires=mp.get_time()+(duration or .85);draw();draw_timer:resume()end
mp.register_script_message('cinema-toast',function(label,value)show(label,value,nil,1.5)end)
local function change_speed(target)
 -- The transport script can still have a ramp/request in flight while the
 -- observed speed equals the original rate. Always send the latest intent;
 -- comparing the applied value here could lose a release or focus-loss reset.
 mp.commandv('script-message-to','cinema','cinema-speed',tostring(target))
end
local function release()
 if timer then timer:kill();timer=nil end
 local original=hold and hold.speed
 if hold and hold.fast then change_speed(original)end
 hold=nil
 return original
end
local function jump(direction)
 -- Setting the absolute position bypasses mpv's continuous-seek delay queue.
 -- hr-seek=always preserves exact five-second targets; rapid short presses
 -- can replace an unfinished seek instead of waiting for its previous frame.
 local position=mp.get_property_number('time-pos')
 if position then mp.set_property_number('time-pos',math.max(0,position+direction*5))end
end
local function seek_key(direction,e)
 if e.event=='repeat' then return end
 if e.event=='down' then
  local original=release();hold={direction=direction,speed=original or mp.get_property_number('user-data/cinema-transport-speed',mp.get_property_number('speed',1)),fast=false}
  timer=mp.add_timeout(.28,function()if hold then hold.fast=true;local speed=mp.get_property_number('user-data/cinema-hold-speed',2);change_speed(speed);show('长按倍速',string.format('%d×',speed),nil,2)end end)
 elseif e.event=='up' then
  if not hold or hold.direction~=direction then return end
  local fast=hold.fast;local original=release()
  if fast then show('播放速度',string.format('%.2g×',original or 1))
  elseif not e.canceled then jump(direction)end
 elseif e.event=='press' and not e.canceled then jump(direction)end
end

local click_start
local function cancel_click()click_start=nil end
local function single_click()
 if not panel_open then mp.commandv('no-osd','cycle','pause')end
end
mp.add_key_binding(nil,'cinema-click',function(e)
 if e.canceled then cancel_click();return end
 if e.event=='down'then local x,y=mp.get_mouse_pos();click_start={x=x,y=y}
 elseif e.event=='up'then local start=click_start;click_start=nil;if start then local x,y=mp.get_mouse_pos();if math.abs(x-start.x)<6 and math.abs(y-start.y)<6 then single_click()end end
 elseif e.event=='press'then single_click()end
end,{complex=true})
-- Double-click fullscreen is intentionally disabled; every primary click is immediate.
mp.add_key_binding(nil,'cinema-double-click',function()end)
mp.observe_property('fullscreen','bool',function()cancel_click()end)
mp.observe_property('user-data/cinema-panel/open','bool',function(_,v)if v then cancel_click()end end)
mp.observe_property('focused','bool',function(_,v)if not v then cancel_click()end end)
mp.register_event('end-file',cancel_click)
mp.register_event('shutdown',cancel_click)

mp.add_key_binding(nil,'cinema-seek-left',function(e)seek_key(-1,e)end,{complex=true})
mp.add_key_binding(nil,'cinema-seek-right',function(e)seek_key(1,e)end,{complex=true})
mp.observe_property('volume','number',function(_,v)if v and last_volume and math.abs(v-last_volume)>.01 then show('音量',string.format('%.0f%%',v),math.max(0,math.min(1,v/100)))end;last_volume=v end)
mp.observe_property('pause','bool',function(_,v)if mp.get_time()>ready_at then show(v and '已暂停' or '继续播放',time(mp.get_property_number('time-pos',0)))end end)
mp.observe_property('user-data/cinema-panel/open','bool',function(_,v)panel_open=v;if v then release();hint=nil;overlay:park();draw_timer:kill()end end)
mp.observe_property('focused','bool',function(_,v)if v==false then release();hint=nil;overlay:park();draw_timer:kill()end end)
mp.observe_property('window-minimized','bool',function(_,v)if v then release();hint=nil;overlay:park();draw_timer:kill()end end)
mp.register_event('seek',function()if mp.get_time()>ready_at then local t=mp.get_property_number('time-pos',0);show('播放进度',time(t)..' / '..time(mp.get_property_number('duration',0)),math.max(0,math.min(1,t/math.max(1,mp.get_property_number('duration',1)))))end end)
mp.register_event('file-loaded',function()release();ready_at=mp.get_time()+.4 end)
mp.register_event('end-file',release)
mp.observe_property('osd-dimensions','native',function(_,v)dimensions=v;if hint then draw()end end)
mp.observe_property('user-data/cinema-ui-size','number',function(_,v)ui_factor=(v or 16)/16;if hint then draw()end end)
mp.observe_property('display-hidpi-scale','number',function(_,v)hidpi=v and v>0 and v or 1;if hint then draw()end end)
mp.observe_property('user-data/cinema-ui-weight','number',function(_,v)ui_weight=v or 400;if hint then draw()end end)
mp.register_event('shutdown',function()release();draw_timer:kill();overlay:park()end)

overlay:prime('input')
