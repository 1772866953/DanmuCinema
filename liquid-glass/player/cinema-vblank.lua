-- Follow the output's refresh signal instead of racing an unrelated Lua timer.
-- This object belongs only to the danmu script; no video/colour pipeline changes.
local mp=require 'mp'
local M={active=false}
local ffi,dxgi,user,output,monitor,wait
local release,invoke
pcall(function()
 ffi=require 'ffi'
 ffi.cdef[[typedef struct {uint32_t a;uint16_t b,c;uint8_t d[8];} CinemaGuid;typedef struct {uint16_t name[32];int32_t rect[4];int32_t attached,rotation;void* monitor;} CinemaOutputDesc;int CreateDXGIFactory(const CinemaGuid*,void**);void* MonitorFromWindow(void*,uint32_t);]]
 dxgi=ffi.load('dxgi');user=ffi.load('user32')
 invoke=function(obj,index,signature)return ffi.cast(signature,ffi.cast('void***',obj)[0][index])end
 release=function(obj)if obj~=nil then invoke(obj,2,'uint32_t (__stdcall *)(void*)')(obj)end end
end)
function M.refresh(force)
 M.active=false
 if not dxgi then return end
 local hwnd=mp.get_property_number('window-id');if not hwnd then return end
 local next_monitor=user.MonitorFromWindow(ffi.cast('void*',hwnd),2)
 if output and next_monitor==monitor and not force then M.active=true;return end
 if output then release(output);output=nil end
 monitor=next_monitor
 local iid,factory=ffi.new('CinemaGuid',{0x7b7166ec,0x21c7,0x44ae,{0xb2,0x1a,0xc9,0xae,0x32,0x1a,0xe3,0x69}}),ffi.new('void*[1]')
 if dxgi.CreateDXGIFactory(iid,factory)~=0 then return end
 for i=0,15 do
  local adapter=ffi.new('void*[1]')
  if invoke(factory[0],7,'int (__stdcall *)(void*,uint32_t,void**)')(factory[0],i,adapter)~=0 then break end
  for j=0,15 do
   local candidate=ffi.new('void*[1]')
   if invoke(adapter[0],7,'int (__stdcall *)(void*,uint32_t,void**)')(adapter[0],j,candidate)~=0 then break end
   local desc=ffi.new('CinemaOutputDesc')
   if invoke(candidate[0],7,'int (__stdcall *)(void*,CinemaOutputDesc*)')(candidate[0],desc)==0 and desc.attached~=0 and desc.monitor==monitor then output=candidate[0];break end
   release(candidate[0])
  end
  release(adapter[0]);if output then break end
 end
 release(factory[0])
 if output then wait=invoke(output,10,'int (__stdcall *)(void*)');M.active=true end
end
function M.wait()
 if not M.active then return false end
 local began=mp.get_time()
 if wait(output)~=0 or mp.get_time()-began>.05 then M.active=false;return false end
 return true
end
mp.register_event('shutdown',function()if output then release(output);output=nil end end)
return M
