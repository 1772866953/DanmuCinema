-- Phase-locked, display-aware UI timer. Only active animations hold a redraw
-- lease; idle windows and tray state do not run a high-frequency timer.
local mp=require 'mp'
local M={}
function M.create(callback,tag)
 local rate,screen,requested=60,60,0
 local running,deadline=false,0
 local frames,started=0,mp.get_time()
 local native,obj
 local function lease(on)mp.commandv('script-message-to','cinema','cinema-ui-activity',tag,on and 'yes'or 'no')end
 local function update()
  rate=math.max(1,math.min(requested>0 and requested or screen,screen))
  if running then native:kill();deadline=mp.get_time()+1/rate;native.timeout=1/rate;native:resume()end
 end
 native=mp.add_timeout(1/60,function()
  frames=frames+1;callback()
  if not running then return end
  local now=mp.get_time();deadline=deadline+1/rate
  if deadline<=now then deadline=deadline+(math.floor((now-deadline)*rate)+1)/rate end
  native.timeout=math.max(.0001,deadline-now);native:resume()
 end);native:kill()
 obj={}
 function obj:resume()if running then return end;running=true;deadline=mp.get_time()+1/rate;native.timeout=1/rate;native:resume();lease(true)end
 function obj:kill()if not running then return end;running=false;native:kill();lease(false)end
 function obj:is_enabled()return running end
 function obj:stats()return{rate=rate,frames=frames,elapsed=mp.get_time()-started,running=running}end
 mp.observe_property('display-fps','number',function(_,v)screen=v and v>0 and v or 60;update()end)
 mp.observe_property('user-data/cinema-ui-fps','number',function(_,v)requested=v or 0;update()end)
 mp.register_event('shutdown',function()running=false;native:kill()end)
 return obj
end
return M
