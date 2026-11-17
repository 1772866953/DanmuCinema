-- Nonblocking, bounded OSD submission. Each overlay owns at most one native
-- command and one latest replacement; stale frames cannot accumulate or reappear
-- after removal. Text is still rendered by libass at native display resolution.
local mp=require 'mp'
local M={}
if mp.get_opt('cinema-external')=='yes' then
 local utils=require 'mp.utils'
 local sink,serial,layers=nil,0,{}
 function M.create(format)
  serial=serial+1
  local overlay,active,pending,disposed={id=mp.get_script_name()..':'..serial,format=format,hidden=true},nil,nil,false
  local stats={submitted=0,completed=0,coalesced=0}
  local send
  send=function(snapshot)
   if disposed then return end
   if not sink or active then if pending then stats.coalesced=stats.coalesced+1 end;pending=snapshot;return end
   stats.submitted=stats.submitted+1
   active=mp.command_native_async({'script-message-to',sink,'cinema-overlay',utils.format_json(snapshot)},function(ok)
    active=nil;stats.completed=stats.completed+1
    if disposed then return end
    local latest=pending;pending=nil;if latest then send(latest)end
   end)
  end
  local function snapshot()return{id=overlay.id,data=overlay.data or '',hidden=overlay.hidden,z=overlay.z or 0,res_x=math.max(0,math.floor(overlay.res_x or 0)),res_y=math.max(1,math.floor(overlay.res_y or 720))}end
  function overlay:update()self.hidden=false;send(snapshot())end
  function overlay:park()if not self.hidden then self.hidden=true;send(snapshot())end end
  function overlay:remove()self.data='';self.hidden=true;send(snapshot())end
  function overlay:stats()return stats end
  function overlay:prime(role)mp.set_property_bool('user-data/cinema-ready-'..role,true)end
  layers[#layers+1]=function()send(snapshot())end
  mp.register_event('shutdown',function()disposed=true;pending=nil;if active then mp.abort_async_command(active)end end)
  return overlay
 end
 mp.register_script_message('cinema-overlay-sink',function(value)sink=value;for _,refresh in ipairs(layers)do refresh()end end)
 return M
end
function M.create(format)
 local overlay=mp.create_osd_overlay(format)
 local active,pending,disposed=nil,nil,false
 local stats={submitted=0,completed=0,coalesced=0}
 local submit
 submit=function(snapshot)
  if disposed then return end
  if active then if pending then stats.coalesced=stats.coalesced+1 end;pending=snapshot;return end
  stats.submitted=stats.submitted+1
  active=mp.command_native_async(snapshot,function(ok,_,err)
   active=nil;stats.completed=stats.completed+1
   if disposed then return end
   if not ok then mp.msg.error('OSD update failed: '..tostring(err))end
   local next=pending;pending=nil;if next then submit(next)end
  end)
 end
 function overlay:update()
  self.hidden=false
  submit({name='osd-overlay',id=self.id,format=self.format,data=self.data or '',res_x=math.max(0,math.floor(self.res_x or 0)),res_y=math.max(0,math.floor(self.res_y or 720)),z=self.z or 0,hidden=self.hidden or false,compute_bounds=false})
 end
 function overlay:remove()self.data='';submit({name='osd-overlay',id=self.id,format='none',data=''})end
 -- Keep libass/font resources resident while an intermittent UI is hidden.
 -- Destroying the native overlay re-enumerates every font on the next hint,
 -- blocking the playback core long enough to underrun its audio device.
 function overlay:park()
  if self.hidden then return end
  self.hidden=true
  submit({name='osd-overlay',id=self.id,format=self.format,data=self.data or '',res_x=math.max(0,math.floor(self.res_x or 0)),res_y=math.max(0,math.floor(self.res_y or 720)),z=self.z or 0,hidden=true,compute_bounds=false})
 end
 function overlay:stats()return stats end
 -- Allocate this overlay's libass/font state before decoding starts. Keeping
 -- it parked afterwards lets a first menu/hint open reuse the same resources.
 function overlay:prime(role)
  self.res_x,self.res_y=1280,720
  local seed='{\\pos(-10000,-10000)\\fnDanmuCinema UI\\fs16}预热'
  self.data=seed;local completed=stats.completed
  self:update()
  local ready
  ready=mp.add_periodic_timer(.01,function()
   if stats.completed<=completed then return end
   ready:kill()
   if self.data==seed and not self.hidden then self:park()end
   mp.set_property_bool('user-data/cinema-ready-'..role,true)
  end)
 end
 mp.register_event('shutdown',function()disposed=true;pending=nil;if active then mp.abort_async_command(active);active=nil end end)
 return overlay
end
return M
