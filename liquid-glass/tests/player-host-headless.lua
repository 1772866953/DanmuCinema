-- Run the real host script against deterministic Win32 geometry, without
-- opening a window or touching another process. This is not an FPS benchmark.
local native,utils=require 'mp',require 'mp.utils'
local opts={script='',report=''};require('mp.options').read_options(opts,'host-test')
local checks,observers,messages,events={},{},{},{}
local reads,moves,publishes=0,{},{}
local now,above,reorders=0,false,0
local bounds={x=50,y=60,w=960,h=540}
local owner_visible,owner_valid,iconic,overlay_visible=true,true,false,false
local attached,timer,fail_move,fail_show
local fake={}
function fake.get_property_bool(_,fallback)reads=reads+1;return fallback end
function fake.get_time()return now end
function fake.set_property_native(_,value)publishes[#publishes+1]=value end
function fake.observe_property(name,_,callback)observers[name]=callback end
function fake.add_periodic_timer(_,callback)timer={callback=callback,on=true};function timer:kill()self.on=false end;return timer end
function fake.register_script_message(name,callback)messages[name]=callback end
function fake.register_event(name,callback)events[name]=callback end
local user={}
function user.IsWindow(h)return (h==99 or h==11 and owner_valid)and 1 or 0 end
function user.IsWindowVisible(h)return(h==99 and overlay_visible or h==11 and owner_visible)and 1 or 0 end
function user.IsIconic()return iconic and 1 or 0 end
function user.GetWindowThreadProcessId(h,pid)pid[0]=h==99 and 8 or 7 end
function user.EnumWindows(callback)callback(11)end
function user.SetWindowLongPtrW(h,index,value)assert(h==99 and index==-8);attached=value end
function user.GetClientRect(_,rect)rect.right=bounds.w;rect.bottom=bounds.h;return 1 end
function user.ClientToScreen(_,point)point.x=bounds.x;point.y=bounds.y;return 1 end
function user.SetWindowPos(h,z,x,y,w,hgt,flags)
 assert(h==99)
 if flags==0x4213 then assert(z==77,'place below the window already above the owner');above=true;reorders=reorders+1;return 1 end
 assert(flags==0x4214,'geometry must post to the owned window thread without activation');if fail_move then return 0 end;moves[#moves+1]={x=x,y=y,w=w,h=hgt};return 1
end
function user.GetWindow(h,command)
 if h==11 then if command==3 then return above and 99 or 77 else return above and nil or 99 end end
 if h==99 and command==2 then return above and 11 or nil end
 return nil
end
function user.ShowWindowAsync(h,command)assert(h==99);if fail_show then return 0 end;overlay_visible=command~=0;return 1 end
local ffi={cdef=function()end}
function ffi.load(name)return name=='user32'and user or{GetCurrentProcessId=function()return 7 end}end
function ffi.new()return{}end
function ffi.cast(kind,value)
 if kind~='CinemaHostEnum'then return value end
 return setmetatable({free=function()end},{__call=function(_,...)return value(...)end})
end
local oldmp,oldffi,oldoptions=package.loaded.mp,package.loaded.ffi,package.loaded['mp.options']
local function check(value,name)assert(value,name);checks[#checks+1]=name end
local ok,error=pcall(function()
 package.loaded.mp=fake;package.loaded.ffi=ffi;package.loaded['mp.options']={read_options=function(o)o.hwnd='99';o.parent_pid=8;o.hidden_until_loaded=true end}
 dofile(opts.script)
 check(attached==11 and not overlay_visible,'attach only the owned window and stay hidden until loaded')
 for i=1,2000 do timer.callback()end
 check(reads==1 and #moves==1 and #publishes==1,'unchanged polls do not read mpv properties or republish geometry')
 observers['user-data/cinema-overlay-visible']('',true)
 check(overlay_visible,'loaded state shows the overlay immediately')
 check(above and reorders==1,'initial display raises only the owned overlay above video without overtaking another application')
 above=false;now=.2;timer.callback();check(above and reorders==2,'native mode-transition reordering is repaired without focus or resize')
 bounds.x=90;bounds.y=100;bounds.w=1280;bounds.h=720;timer.callback()
 check(#moves==2 and moves[2].x==90 and moves[2].h==720,'move and resize stay synchronized on the next geometry sample')
 iconic=true;timer.callback();check(not overlay_visible,'minimize hides the overlay')
 iconic=false;timer.callback();check(overlay_visible and #moves==2,'restore reuses unchanged geometry and shows the overlay')
 observers['user-data/cinema-overlay-visible']('',false);check(not overlay_visible,'loaded-state withdrawal hides immediately')
 observers['user-data/cinema-overlay-visible']('',true);owner_visible=false;timer.callback();check(not overlay_visible,'hidden owner never leaves the overlay visible')
 owner_visible=true;timer.callback();check(overlay_visible,'visible owner restores the overlay')
 bounds.x=120;fail_move=true;timer.callback();check(#moves==2,'failed geometry submission is not cached as successful')
 fail_move=false;timer.callback();check(#moves==3 and moves[3].x==120,'failed geometry is retried on the next sample')
 fail_show=true;iconic=true;timer.callback();check(overlay_visible,'failed visibility submission keeps the prior state')
 fail_show=false;timer.callback();check(not overlay_visible,'failed visibility is retried on the next sample')
 events.shutdown();check(not timer.on and not overlay_visible and attached==0,'shutdown stops polling and detaches the owned overlay')
end)
package.loaded.mp,package.loaded.ffi,package.loaded['mp.options']=oldmp,oldffi,oldoptions
local file=assert(io.open(opts.report,'wb'));file:write(utils.format_json({passed=ok,checks=checks,error=not ok and tostring(error)or nil,propertyReads=reads,moves=#moves,publishes=#publishes}));file:close()
native.add_timeout(0,function()native.commandv('quit',ok and '0'or '1')end)
