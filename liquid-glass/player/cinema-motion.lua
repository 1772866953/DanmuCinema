-- Time-based scalar transitions. No scaling or bitmap caching of glyphs.
local mp=require 'mp'
local M,values={},{}
function M.get(key,target,duration,initial)
 local now=mp.get_time();local v=values[key]
 if not v then v={from=initial==nil and target or initial,target=target,start=now,duration=duration or .16,value=initial==nil and target or initial};values[key]=v end
 local p=math.max(0,math.min(1,(now-v.start)/v.duration));v.value=v.from+(v.target-v.from)*(1-(1-p)^3)
 if target~=v.target then v.from=v.value;v.target=target;v.start=now;v.duration=duration or .16 end
 v.seen=now
 return v.value
end
function M.active()
 local active=false;local now=mp.get_time()
 for key,v in pairs(values)do if now-v.seen>4 then values[key]=nil elseif now-v.start<v.duration and math.abs(v.target-v.from)>.0001 then active=true end end
 return active
end
function M.reset(prefix)
 for k in pairs(values)do if k:sub(1,#prefix)==prefix then values[k]=nil end end
end
return M
