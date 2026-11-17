-- A private high-resolution one-shot timer for the danmu thread. Other Lua
-- scripts and the video/audio threads remain free to process input and decode.
local mp=require 'mp'
local M={available=false}
local ffi,api,handle,due
pcall(function()
 ffi=require 'ffi'
 ffi.cdef[[void* CreateWaitableTimerExW(void*,const uint16_t*,uint32_t,uint32_t);int SetWaitableTimerEx(void*,const int64_t*,int32_t,void*,void*,void*,uint32_t);uint32_t WaitForSingleObject(void*,uint32_t);int CloseHandle(void*);]]
 api=ffi.load('kernel32');handle=api.CreateWaitableTimerExW(nil,nil,2,0x1F0003)
 if handle==nil then return end
 due=ffi.new('int64_t[1]');M.available=true
end)
function M.wait(deadline)
 if not M.available then return end
 local remaining=deadline-mp.get_time()
 if remaining<=0 or remaining>.0031 then return end
 if remaining>.00012 then
  due[0]=-math.max(1,math.floor((remaining-.00008)*10000000))
  if api.SetWaitableTimerEx(handle,due,0,nil,nil,nil,0)~=0 then api.WaitForSingleObject(handle,4)end
 end
 -- At most 120 us of fine alignment, never an unbounded busy wait.
 local began=mp.get_time()
 while mp.get_time()<deadline and mp.get_time()-began<.00012 do end
end
mp.register_event('shutdown',function()if handle~=nil and api then api.CloseHandle(handle);handle=nil end end)
return M
