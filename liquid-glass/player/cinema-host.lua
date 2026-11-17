-- Owned overlay. Only this mpv process and its verified parent
-- window are touched. No global topmost window, focus stealing or video hook.
local mp,ffi=require 'mp',require 'ffi'
local opts={hwnd='',parent_pid=0,hidden_until_loaded=false};require('mp.options').read_options(opts,'cinema_host')
ffi.cdef[[
typedef struct { long left,top,right,bottom; } CinemaHostRect;
typedef struct { long x,y; } CinemaHostPoint;
typedef int (__stdcall *CinemaHostEnum)(void*,intptr_t);
int EnumWindows(CinemaHostEnum,intptr_t);
unsigned long GetWindowThreadProcessId(void*,unsigned long*);
unsigned long GetCurrentProcessId(void);
int IsWindow(void*); int IsWindowVisible(void*); int IsIconic(void*);
int GetClientRect(void*,CinemaHostRect*); int ClientToScreen(void*,CinemaHostPoint*);
intptr_t SetWindowLongPtrW(void*,int,intptr_t);
int SetWindowPos(void*,void*,int,int,int,int,unsigned int);
int ShowWindowAsync(void*,int);
void* GetWindow(void*,unsigned int);
]]
local user,kernel=ffi.load('user32'),ffi.load('kernel32')
local ownpid,overlay,owner=kernel.GetCurrentProcessId(),ffi.cast('void*',tonumber(opts.hwnd)),nil
local pid=ffi.new('unsigned long[1]')
if overlay==nil or user.IsWindow(overlay)==0 then error('Overlay window does not exist')end
user.GetWindowThreadProcessId(overlay,pid)
if tonumber(pid[0])~=opts.parent_pid then error('Overlay must belong to the launching application')end
local last,visible=nil,user.IsWindowVisible(overlay)~=0
local last_x,last_y,last_w,last_h
local overlay_loaded=not opts.hidden_until_loaded or mp.get_property_bool('user-data/cinema-overlay-visible',false)
local function publish(value)mp.set_property_native('user-data/cinema-host',value)end
local function locate()
 local callback=ffi.cast('CinemaHostEnum',function(h)
  user.GetWindowThreadProcessId(h,pid)
  if pid[0]==ownpid and user.IsWindowVisible(h)~=0 then owner=h;return 0 end
  return 1
 end)
 user.EnumWindows(callback,0);callback:free()
 if owner then user.SetWindowLongPtrW(overlay,-8,ffi.cast('intptr_t',owner))end
end
local rect,point=ffi.new('CinemaHostRect'),ffi.new('CinemaHostPoint')
local order_check=0
local function keep_above_owner()
 -- Keep the pane immediately above this owner, below whichever application
 -- already precedes it. Never use HWND_TOPMOST or activate either window.
 if user.GetWindow(owner,3)==overlay then return end
 local now=mp.get_time();if now<order_check then return end;order_check=now+.1
 local below=user.GetWindow(owner,2)
 for i=1,256 do
  if below==nil then return end
  if below==overlay then
   user.SetWindowPos(overlay,user.GetWindow(owner,3),0,0,0,0,0x4213)
   return
  end
  below=user.GetWindow(below,2)
 end
end
local function update()
 if user.IsWindow(overlay)==0 then return end
 if not owner then locate();if not owner then return end end
 local drawable=user.IsWindow(owner)~=0 and user.IsWindowVisible(owner)~=0 and user.IsIconic(owner)==0
 local show=drawable and overlay_loaded
 if drawable and user.GetClientRect(owner,rect)~=0 then
  point.x,point.y=0,0;user.ClientToScreen(owner,point)
  local w,h=tonumber(rect.right),tonumber(rect.bottom)
  local x,y=tonumber(point.x),tonumber(point.y)
  if w>0 and h>0 and (x~=last_x or y~=last_y or w~=last_w or h~=last_h) then
   -- The overlay belongs to Electron's window thread. Do not wait for its
   -- WM_SIZE/graphics work on mpv's script thread during a mode transition.
   if user.SetWindowPos(overlay,nil,point.x,point.y,w,h,0x4214)~=0 then
    last_x,last_y,last_w,last_h=x,y,w,h
    last=string.format('%d:%d:%d:%d',x,y,w,h)
    publish({attached=true,visible=show,x=x,y=y,width=w,height=h})
   end
  end
 end
 if show~=visible and user.ShowWindowAsync(overlay,show and 4 or 0)~=0 then visible=show;publish({attached=true,visible=show,bounds=last})end
 if show then keep_above_owner()end
end
-- Observe visibility once; the geometry poll must not traverse mpv's property
-- tree or allocate a bounds string 125 times a second while nothing changes.
mp.observe_property('user-data/cinema-overlay-visible','bool',function(_,value)
 overlay_loaded=not opts.hidden_until_loaded or value==true
 update()
end)
local timer=mp.add_periodic_timer(.008,update)
local function detach()timer:kill();if user.IsWindow(overlay)~=0 then user.ShowWindowAsync(overlay,0);user.SetWindowLongPtrW(overlay,-8,0)end end
mp.register_script_message('cinema-host-detach',detach)
mp.register_event('shutdown',detach)
update()
