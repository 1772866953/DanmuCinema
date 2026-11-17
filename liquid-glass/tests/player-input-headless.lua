-- Exercise the real input script with deterministic clocks. Run inside mpv's
-- Lua runtime, with null outputs and no file/window. Never a rendering benchmark.
local native=require 'mp'
local utils=require 'mp.utils'
local opts={root='',report=''};require('mp.options').read_options(opts,'input-test')
local calls,timers,bindings,observers,events,messages={},{},{},{},{},{}
local properties={speed=1,['time-pos']=20,['user-data/cinema-transport-speed']=1,['user-data/cinema-hold-speed']=6}
local now=0
local fake={msg=native.msg}
function fake.get_time()return now end
function fake.get_opt()return nil end
function fake.get_property_number(key,fallback)return properties[key] or fallback end
function fake.get_property(key,fallback)local value=properties[key];return value~=nil and tostring(value)or fallback end
function fake.set_property_native(key,value)properties[key]=value end
fake.set_property_number=fake.set_property_native
function fake.get_mouse_pos()return 0,0 end
function fake.create_osd_overlay()return{id=1}end
function fake.commandv(...)calls[#calls+1]={...}end
function fake.command_native_async(_,cb)cb(true);return 1 end
function fake.add_timeout(timeout,callback)
 local timer={timeout=timeout,callback=callback,on=true}
 function timer:kill()self.on=false end
 function timer:resume()self.on=true end
 function timer:is_enabled()return self.on end
 timers[#timers+1]=timer;return timer
end
fake.add_periodic_timer=fake.add_timeout
function fake.add_key_binding(_,name,callback)bindings[name]=callback end
function fake.observe_property(key,_,callback)observers[key]=callback end
function fake.register_event(key,callback)events[key]=callback end
function fake.register_script_message(name,callback)messages[name]=callback end
local checks={}
local function check(ok,message)assert(ok,message);checks[#checks+1]=message end
local function speeds()
 local found={};for _,call in ipairs(calls)do if call[1]=='script-message-to'and call[3]=='cinema-speed'then found[#found+1]=tonumber(call[4])end end;return found
end
local function down(direction)
 bindings['cinema-seek-'..(direction or 'right')]({event='down'})
end
local function fire_hold()
 for i=#timers,1,-1 do local t=timers[i];if t.on and t.timeout==.28 then now=now+.28;t.on=false;t.callback();return end end
 error('No pending hold')
end
local function reset()calls={} end
local function run()
 package.loaded.mp=fake
 dofile(opts.root..'/player/cinema-input.lua')
 reset();down();fire_hold();bindings['cinema-seek-right']({event='up'})
 local s=speeds();check(#s==2 and s[1]==6 and s[2]==1,'release restores original rate even before the first ramp tick')
 reset();down();fire_hold();observers.focused(nil,false)
 s=speeds();check(#s==2 and s[2]==1,'focus loss cancels a queued hold even when applied speed is still original')
 reset();down();fire_hold();observers['window-minimized'](nil,true)
 s=speeds();check(#s==2 and s[2]==1,'minimize cancels a queued hold')
 reset();down();fire_hold();observers['user-data/cinema-panel/open'](nil,true)
 s=speeds();check(#s==2 and s[2]==1,'opening settings restores original speed')
 observers['user-data/cinema-panel/open'](nil,false)
 reset();down();fire_hold();down('left');fire_hold();bindings['cinema-seek-left']({event='up'})
 s=speeds();check(#s==4 and s[2]==1 and s[4]==1,'changing direction restores and reuses the true pre-hold speed')
 reset();down();bindings['cinema-seek-right']({event='up'})
 check(#speeds()==0 and properties['time-pos']==25,'short right press seeks five seconds without changing speed')
 reset();down('left');bindings['cinema-seek-left']({event='up',canceled=true})
 check(#speeds()==0 and properties['time-pos']==25,'cancelled short press performs neither seek nor speed change')
 reset();down();bindings['cinema-seek-right']({event='repeat'});fire_hold();bindings['cinema-seek-right']({event='up'})
 check(#speeds()==2,'key repeat does not multiply hold requests')
 properties.speed=1.5;properties['user-data/cinema-transport-speed']=1.5
 reset();down();fire_hold();events['end-file']()
 s=speeds();check(#s==2 and s[2]==1.5,'end of file restores the actual pre-hold speed')
 -- Test the receiving transport too, rather than only checking emitted intent.
 properties.speed=1;properties['user-data/cinema-transport-speed']=1
 dofile(opts.root..'/player/cinema.lua')
 messages['cinema-speed']('6');messages['cinema-speed']('1')
 local active=0;for _,t in ipairs(timers)do if t.on and t.timeout==.008 then active=active+1 end end
 check(active==0 and properties.speed==1,'receiving transport cancels the old ramp before any speed equality early return')
 messages['cinema-speed']('6');now=now+.3
 for _,t in ipairs(timers)do if t.on and t.timeout==.008 then t.callback()end end
 check(properties.speed==6,'the latest uncancelled hold still reaches the requested speed')
 messages['cinema-speed']('1');now=now+.3
 for _,t in ipairs(timers)do if t.on and t.timeout==.008 then t.callback()end end
 check(properties.speed==1,'release after a completed ramp restores the original speed')
 local before=#timers;messages['cinema-speed']('1')
 check(#timers==before,'repeated original-rate requests do not allocate another speed ramp')
 local function advance(delta)
  now=now+delta
  for _,t in ipairs(timers)do if t.on and t.timeout==.008 then t.callback()end end
 end
 properties['display-fps']=200;observers['display-fps']()
 for _,fps in ipairs({24,60})do
  properties['container-fps']=fps
  for _,rate in ipairs(fps==24 and {2,3,4,5,6}or {2,3})do
   properties.speed=rate;messages['cinema-speed']('1');advance(.05)
   local previous=properties.speed
   assert(previous>1 and previous<rate,'recovery collapsed or moved in the wrong direction')
   for _=1,200 do
    advance(.008);local value=properties.speed
    assert(value>=1 and value<=previous+.000001,'recovery overshot or reversed');previous=value
   end
   check(properties.speed==1,string.format('%dfps %dx recovery is gradual, monotonic and reaches the original rate',fps,rate))
  end
 end
 properties.speed=3;messages['cinema-speed']('1');advance(.05);messages['cinema-speed']('6');advance(.3)
 check(properties.speed==6,'a new hold during recovery cancels the old settling timer')
 properties.speed=3;messages['cinema-speed']('1');advance(.05);observers.pause(nil,true);messages['cinema-speed']('1')
 check(properties.speed==1,'pausing during recovery permits an immediate restore without leaving a stale ramp')
end
local ok,err=xpcall(run,debug.traceback);package.loaded.mp=native
local report={passed=ok,checks=checks,error=not ok and err or nil,performanceVerified=false}
local file=assert(io.open(opts.report,'wb'));file:write(utils.format_json(report));file:close()
native.commandv('quit',ok and 0 or 1)
