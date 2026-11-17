-- DanmuCinema native playback overlays. Video and subtitles remain in mpv's VO.
local mp = require 'mp'
local utils = require 'mp.utils'
local settings_options = {parent_pid = 0,qa=false,vblank_clock=true,external=false}
require('mp.options').read_options(settings_options, 'cinema')
local async_osd=dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-osd.lua')
local overlay = async_osd.create('ass-events')
overlay.z = 20
local scene = nil
local ui_activity={}
local osd_dimensions,audio_delay=nil,0
local audio_anchor,audio_at=nil,0
local clock_value,clock_at=nil,0
local paused = false
local buffering = false
local minimized = false
local seeking = false
local enabled = true
local anchor = 0
local anchored_at = mp.get_time()
local speed = 1
local changing_speed=false
local offset = 0
local last_data = ''
local requested_fps, effective_fps,display_rate = 60, 60,60
local function update_interval()return 1/effective_fps end
local deadline_timer=settings_options.vblank_clock and dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-deadline.lua')or nil
local vblank_clock=settings_options.vblank_clock and dofile(select(1,utils.split_path(debug.getinfo(1,'S').source:gsub('^@',''))) .. '/cinema-vblank.lua')or nil
local has_motion = false
local render_count = 0
local render_cost,render_peak,render_skipped=0,0,0
local clock_probe={backwards=0,maxBackstep=0,maxStepError=0}
local probe_time,probe_at
local trace,reset_events=nil,{}
local test_marker=false
local started_at = mp.get_time()
local timer
local next_frame = 0
local precision_active,winmm=false,nil
local power,process,prior_power,ffi_power
pcall(function()local ffi=require 'ffi';ffi.cdef[[unsigned int timeBeginPeriod(unsigned int);unsigned int timeEndPeriod(unsigned int);void* GetCurrentProcess(void);int GetProcessInformation(void*,int,void*,unsigned long);int SetProcessInformation(void*,int,void*,unsigned long);typedef struct {unsigned long Version;unsigned long ControlMask;unsigned long StateMask;} CinemaPowerThrottle;]];winmm=ffi.load('winmm');power=ffi.load('kernel32');ffi_power=ffi;process=power.GetCurrentProcess();prior_power=ffi.new('CinemaPowerThrottle');prior_power.Version=1;if power.GetProcessInformation(process,4,prior_power,ffi.sizeof(prior_power))==0 then prior_power=nil end end)
local function precision(on)
    on=on and winmm~=nil
    if on==precision_active then return end
    if on then
        if prior_power then local state=ffi_power.new('CinemaPowerThrottle');local bit=require 'bit';state.Version=1;state.ControlMask=bit.bor(prior_power.ControlMask,5);state.StateMask=bit.band(prior_power.StateMask,bit.bnot(5));power.SetProcessInformation(process,4,state,ffi_power.sizeof(state))end
        precision_active=winmm.timeBeginPeriod(1)==0
    else if precision_active then winmm.timeEndPeriod(1)end;if prior_power then power.SetProcessInformation(process,4,prior_power,ffi_power.sizeof(prior_power))end;precision_active=false end
end
local watchdog, parent_handle, kernel
local parent_heartbeat = 0
if settings_options.parent_pid > 0 then
    local success = pcall(function()
        local ffi = require 'ffi'
        ffi.cdef[[void* OpenProcess(unsigned long access, int inherit, unsigned long pid);
            unsigned long WaitForSingleObject(void* handle, unsigned long milliseconds);
            int CloseHandle(void* handle);]]
        kernel = ffi.load('kernel32')
        parent_handle = kernel.OpenProcess(0x00100000, 0, settings_options.parent_pid)
        if parent_handle == nil then error('No parent process handle') end
    end)
    mp.set_property_bool('user-data/cinema-parent-watchdog', success)
    watchdog = mp.add_periodic_timer(1, function()
        if success and kernel.WaitForSingleObject(parent_handle, 0) == 0 then mp.commandv('quit')
        elseif not success and parent_heartbeat > 0 and mp.get_time()-parent_heartbeat > 10 then mp.commandv('quit') end
    end)
end
mp.register_script_message('cinema-heartbeat', function() parent_heartbeat=mp.get_time() end)
local function canonical(value) return (value or ''):gsub('\\', '/'):lower() end
local function reset_clock(reason)
    if settings_options.qa then reset_events[#reset_events+1]={at=mp.get_time(),reason=reason}end
    anchor = mp.get_property_number('time-pos', 0)
    anchored_at = mp.get_time()
    audio_anchor=mp.get_property_number('audio-pts');audio_at=anchored_at
    clock_value=nil;probe_time=nil
end
local function media_time(sample_at)
    if paused or buffering or seeking then return anchor end
    local now=math.max(clock_at,sample_at or mp.get_time())
    local target=not changing_speed and audio_anchor and audio_anchor+audio_delay+math.min(.25,math.max(0,now-audio_at))*speed
        or anchor+math.min(.25,math.max(0,now-anchored_at))*speed
    -- Notifications can arrive late. Correct their delivery jitter gradually,
    -- without blocking the render loop on a synchronous property query or
    -- stepping a scrolling comment backwards. Explicit transport changes reset
    -- the clock, so seek/pause/speed do not inherit a smoothing delay.
    if not clock_value then clock_value=target else
        local step=math.max(0,now-clock_at)*speed
        local predicted=clock_value+step
        local limit=step*.03
        clock_value=predicted+math.max(-limit,math.min(limit,target-predicted))
    end
    clock_at=now
    return clock_value
end
local function rgb(color)
    local r = math.floor(color / 65536) % 256
    local g = math.floor(color / 256) % 256
    local b = color % 256
    return string.format('%02X%02X%02X', b, g, r)
end
local function geometry()
    local d = osd_dimensions
    if not d or not d.w or d.w <= 0 or not d.h or d.h <= 0 then return nil end
    local scale = 1280 / d.w
    return {w = 1280, h = d.h * scale, left = (d.ml or 0)*scale, top = (d.mt or 0)*scale,
        width = (d.w-(d.ml or 0)-(d.mr or 0))*scale, height = (d.h-(d.mt or 0)-(d.mb or 0))*scale, pixel = scale}
end
local function visible(t)
    local result = {}
    if not scene or not enabled then return result end
    local items = scene.items
    local lo, hi = 1, #items + 1
    while lo < hi do
        local mid = math.floor((lo + hi) / 2)
        if mid <= #items and items[mid].t < t-(scene.duration or 32) then lo = mid+1 else hi = mid end
    end
    for i = lo, #items do
        local item = items[i]
        if item.t > t then break end
        if t < item.t+item.d then
            local f = (t-item.t)/item.d
            local x = item.m == 'scroll' and (scene.width-(scene.width+item.w)*f)
                or item.m == 'reverse' and (-item.w+(scene.width+item.w)*f) or scene.width/2
            result[#result+1] = {item = item, x = x}
        end
    end
    return result
end
-- Styles do not change as comments move. Build them once per scene/geometry,
-- rather than allocating a visibility table and formatting every ASS attribute
-- for every comment at 240 Hz. Only positions change on the hot path.
local style_scene,style_geometry,style_opacity=nil,'',nil
local styles,lines={},{}
local function render(sample_at)
    local began=settings_options.qa and mp.get_time()
    local g = geometry()
    if not g or not scene or not enabled or minimized then
        if last_data ~= '' then overlay:park(); last_data = '' end
        return
    end
    local current_time=media_time(sample_at)
    if trace and #trace<120000 then trace[#trace+1]={at=began,time=current_time,audioAge=began-audio_at,audio=audio_anchor}end
    if began then
        if probe_time and not paused and not buffering and not seeking then local delta=current_time-probe_time;clock_probe.maxStepError=math.max(clock_probe.maxStepError,math.abs(delta-(began-probe_at)*speed));if delta<0 then clock_probe.backwards=clock_probe.backwards+1;clock_probe.maxBackstep=math.max(clock_probe.maxBackstep,-delta)end end
        probe_time,probe_at=current_time,began
    end
    local t = current_time - offset
    local sx, sy = g.width/scene.width, g.height/scene.height
    local alpha = math.floor((1-scene.opacity)*255+.5)
    local clip = string.format('\\clip(%.2f,%.2f,%.2f,%.2f)', g.left, g.top, g.left+g.width, g.top+g.height)
    local signature=string.format('%s:%.5f:%.5f',clip,sx,sy)
    if style_scene~=scene or style_geometry~=signature or style_opacity~=alpha then
        styles={};style_scene,style_geometry,style_opacity=scene,signature,alpha
    end
    for i=#lines,1,-1 do lines[i]=nil end
    local items=scene.items
    local lo,hi=1,#items+1
    while lo<hi do local mid=math.floor((lo+hi)/2);if mid<=#items and items[mid].t<t-(scene.duration or 32)then lo=mid+1 else hi=mid end end
    for i=lo,#items do
        local c=items[i];if c.t>t then break end
        if t<c.t+c.d then
            local style=styles[i]
            if not style then
                style=string.format('\\fs%.2f\\fnDanmuCinema UI\\b%d\\bord1.1\\shad0\\3c&H000000&\\1c&H%s&\\alpha&H%02X&%s}%s',scene.fontSize*sx,scene.fontWeight or 400,rgb(c.c),alpha,clip,c.text);styles[i]=style
            end
            local f=(t-c.t)/c.d
            local x=c.m=='scroll' and scene.width-(scene.width+c.w)*f or c.m=='reverse' and -c.w+(scene.width+c.w)*f or scene.width/2
            lines[#lines+1]=string.format('{\\an%d\\pos(%.2f,%.2f)',(c.m=='top' or c.m=='bottom')and 8 or 7,g.left+x*sx,g.top+c.y*sy)..style
        end
    end
    if settings_options.qa and test_marker then
        local x=1280-(current_time%12)*100
        lines[#lines+1]=string.format('{\\an7\\pos(%.2f,90)\\bord0\\shad0\\1c&HFF00FF&\\p1}m 0 0 l 20 0 20 20 0 20',x)
    end
    local data = table.concat(lines, '\n')
    if data ~= last_data or overlay.res_y ~= g.h then
        overlay.res_x, overlay.res_y, overlay.data = g.w, g.h, data
        overlay:update()
        render_count = render_count+1
        if began then local cost=(mp.get_time()-began)*1000;render_cost=render_cost+cost;render_peak=math.max(render_peak,cost)end
        last_data = data
    end
end
local function refresh_rate()
    local display = mp.get_property_number('display-fps',60)
    local prior=display_rate
    display_rate=display>0 and display or 60
    effective_fps = requested_fps==0 and display_rate or math.min(requested_fps,display_rate)
    if vblank_clock then vblank_clock.refresh(prior~=display_rate)end
    if timer then
        local running = timer:is_enabled()
        timer:kill(); timer.timeout = update_interval(); next_frame = mp.get_time()+timer.timeout
        if running then timer:resume() end
        if running then precision(effective_fps>60)end
    end
end
local output_sync=mp.get_property('video-sync','audio')
local function set_sync(mode)
    if mode~=output_sync then mp.set_property('video-sync',mode);output_sync=mode end
end
-- UI animation timers may release and reacquire their lease in one turn.
-- Do not rebuild the VO/audio synchronizer between those adjacent messages.
local sync_release=mp.add_timeout(.3,function()set_sync('audio')end);sync_release:kill()
local restore_speed_sync,speed_ramp
local function schedule(redraw)
    local active = not settings_options.external and scene and enabled and #scene.items > 0 and not minimized
    -- Display-sync asks the VO to redraw the OSD between decoded video frames.
    -- No interpolation/filter/frame-rate conversion is applied to the video.
    -- Bitstream audio cannot be resampled: use the native audio-clock mode that
    -- repeats/drops video presentation slots while preserving the audio stream.
    local passthrough = mp.get_property('audio-spdif', '') ~= ''
    local sync = not settings_options.external and not changing_speed and (active and has_motion or not minimized and next(ui_activity)~=nil) and (passthrough and 'display-vdrop' or 'display-resample') or 'audio'
    if sync=='audio' and output_sync~='audio' then
        if not sync_release:is_enabled()then sync_release:resume()end
    else sync_release:kill();set_sync(sync)end
    precision(active and not paused and not buffering and not seeking and effective_fps>60 or not minimized and next(ui_activity)~=nil and display_rate>60)
    if active and not paused and not buffering and not seeking then
        if not timer:is_enabled() then next_frame=mp.get_time()+update_interval();timer.timeout=update_interval();timer:resume() end
    else timer:kill()end
    if redraw~=false then render()end
end

restore_speed_sync=mp.add_timeout(.5,function()
 changing_speed=false
 schedule(false)
end);restore_speed_sync:kill()
mp.register_script_message('cinema-speed',function(value)
 local target=tonumber(value);if not target or target<.01 or target>100 then return end
 -- A release can overtake the first ramp tick. Cancel the old request before
 -- testing the applied speed, so it cannot accelerate after the key is up.
 if speed_ramp then speed_ramp:kill();speed_ramp=nil end
 local initial=mp.get_property_number('speed',1)
 mp.set_property_number('user-data/cinema-transport-speed',target)
 if math.abs(initial-target)<.000001 then
  if changing_speed then restore_speed_sync:kill();restore_speed_sync:resume()end
  return
 end
 -- Keep the native audio clock accurate while changing rate. Temporarily
 -- raising autosync smooths away the queued-audio correction and accumulates
 -- an A/V error on release, forcing the VO to catch up by dropping frames.
 changing_speed=true;restore_speed_sync:kill();set_sync('audio')
 if paused then mp.set_property_number('speed',target);restore_speed_sync:resume();return end
 local started=mp.get_time()
 -- Queued audio still carries the old playback rate during recovery. Bound
 -- its clock correction by the available display margin instead of forcing
 -- the VO to catch up faster than the display can present. Acceleration and
 -- rates already above display capacity retain the original curve.
 local video_fps=mp.get_property_number('container-fps',0)
 local ceiling=video_fps>0 and display_rate/video_fps*.98 or 0
 local bounded=target<initial and ceiling>initial+.01
 local ratio=bounded and math.log((ceiling-target)/(ceiling-initial))or 0
 -- Ample headroom (for example 24fps video) must not collapse recovery to a
 -- few milliseconds. Retain at least the original 220ms settling interval.
 local latency=math.max(.12,mp.get_property_number('audio-buffer',.05)*2,bounded and .22/ratio or 0)
 local duration=bounded and latency*ratio or .22
 speed_ramp=mp.add_periodic_timer(.008,function()
  local elapsed=mp.get_time()-started
  local t=math.min(1,elapsed/duration)
  local value=bounded and ceiling-(ceiling-initial)*math.exp(elapsed/latency)or math.exp(math.log(initial)+(math.log(target)-math.log(initial))*t)
  mp.set_property_number('speed',t==1 and target or value)
  if t==1 then speed_ramp:kill();speed_ramp=nil;restore_speed_sync:resume()end
 end)
end)

-- One-shot, phase-locked deadlines avoid accumulating the render cost in
-- every interval (a periodic Lua timer otherwise drifts badly at 120/240 Hz).
timer = mp.add_timeout(1/60, function()
    local following=vblank_clock and deadline_timer and deadline_timer.available and effective_fps>60 and effective_fps>=display_rate*.99 and vblank_clock.wait()
    if following then deadline_timer.wait(mp.get_time()+math.min(.003,.4/effective_fps))end
    local now, interval = mp.get_time(), update_interval()
    -- Sample motion on the presentation cadence, not at the variable moment
    -- Windows happened to wake this Lua thread. Missed slots advance in whole
    -- intervals; a late callback cannot push text forward and then correct it.
    local sampled_at=next_frame+math.max(0,math.floor((now-next_frame)/interval))*interval
    render(following and now or sampled_at)
    now=mp.get_time()
    next_frame = next_frame+interval
    if next_frame <= now then local skipped=math.floor((now-next_frame)/interval)+1;render_skipped=render_skipped+skipped;next_frame=next_frame+skipped*interval end
    -- Let asynchronous command replies drain before waiting for the next
    -- refresh. Immediately re-entering a blocking wait delays acknowledgements
    -- and makes the bounded overlay unnecessarily coalesce fresh positions.
    timer.timeout=following and math.min(.001,interval*.2)or math.max(.0001,next_frame-now)
    timer:resume()
end)
timer:kill()
local function option(key, value)
    mp.commandv('script-message', 'cinema-option', key, tostring(value))
end
local function status(label,value)
    if settings_options.external then mp.commandv('script-message-to','cinema_input','cinema-toast',label,value)
    else mp.osd_message(label..'：'..value,1.2)end
end
local function toggle()
    enabled = not enabled
    option('enabled', enabled)
    schedule()
    status('弹幕',enabled and '已开启' or '已关闭')
end
local function shift(value)
    offset = offset+value
    option('offset', offset)
    schedule()
    status('弹幕偏移',string.format('%s %.1f 秒', offset >= 0 and '延后' or '提前', math.abs(offset)))
end
local function settings() mp.commandv('script-message-to', 'select', 'cinema-settings') end
mp.register_script_message('cinema-ui-activity',function(key,value)ui_activity[key]=value=='yes'and true or nil;schedule(false)end)
mp.register_script_message('cinema-ui-option',function(key,value)option(key,tonumber(value))end)
mp.register_script_message('cinema-options',function(json)
    local value=utils.parse_json(json);if not value then return end
    mp.commandv('script-message-to','select','cinema-state',json)
    if type(value.holdSpeed)=='number' then mp.set_property_number('user-data/cinema-hold-speed',value.holdSpeed)end
    if type(value.uiFontSize)=='number' then mp.set_property_number('user-data/cinema-ui-size',value.uiFontSize)end
    if type(value.uiFps)=='number' then mp.set_property_number('user-data/cinema-ui-fps',value.uiFps)end
    if type(value.uiFontWeight)=='number' then mp.set_property_number('user-data/cinema-ui-weight',value.uiFontWeight) end
    if type(value.enabled)=='boolean' then enabled=value.enabled end
    if scene and type(value.opacity)=='number' then scene.opacity=value.opacity end
    if type(value.offset)=='number' then offset=value.offset end
    local fps=value.danmuFps==240 and 0 or value.danmuFps
    if fps~=requested_fps and (fps==0 or fps==60 or fps==120)then requested_fps=fps;refresh_rate()end
    if type(value.region)=='number' then mp.set_property_number('user-data/cinema-region',value.region) end
    schedule()
end)
mp.register_script_message('cinema-load',function(file,video)
    if settings_options.external then return end
    if canonical(mp.get_property('path')) ~= canonical(video) then return end
    local stream=io.open(file,'rb');if not stream then return end
    local content=stream:read(40*1024*1024+1);stream:close()
    if #content>40*1024*1024 then mp.msg.error('Danmu scene exceeds limit');return end
    local data=utils.parse_json(content)
    if not data or data.version~=1 or type(data.items)~='table' then mp.msg.error('Invalid danmu scene');return end
    scene=data;enabled=data.enabled;offset=data.offset or 0;last_data='';has_motion=false
    for _,item in ipairs(data.items) do if item.m=='scroll' or item.m=='reverse' then has_motion=true;break end end
    schedule()
    mp.set_property_native('user-data/danmucinema',{count=data.count,displayed=data.displayed,offset=offset,enabled=enabled,width=data.width,height=data.height,fontSize=data.fontSize,fontWeight=data.fontWeight,duration=data.duration})
end)
mp.register_script_message('cinema-clear',function() scene=nil;has_motion=false;overlay:park();last_data='';timer:kill();schedule() end)
mp.register_script_message('cinema-probe',function(sequence)
    local time=media_time()
    -- Sample the reference in this Lua turn: a later IPC query can be a whole
    -- video frame (or 6x that interval) newer than this diagnostic snapshot.
    local native_audio=mp.get_property_number('audio-pts')
    local native_time=not paused and native_audio and native_audio+audio_delay or mp.get_property_number('time-pos',0)
    local poses={};for _,v in ipairs(visible(time-offset)) do poses[#poses+1]={t=v.item.t,x=v.x,y=v.item.y,text=v.item.text} end
    mp.set_property_native('user-data/cinema-probe',{sequence=sequence,time=time,nativeTime=native_time,offset=offset,paused=paused,buffering=buffering,active=poses,timer=timer:is_enabled(),geometry=geometry(),fps=requested_fps,effectiveFps=effective_fps,renders=render_count,elapsed=mp.get_time()-started_at,speed=speed,clock=clock_probe,sync=mp.get_property('video-sync'),renderCost=render_cost,renderPeak=render_peak,skipped=render_skipped,precision=precision_active,powerGuard=prior_power~=nil,overlay=overlay.stats and overlay:stats()or nil})
end)
mp.register_script_message('cinema-trace',function(action,file)
    if not settings_options.qa then return end
    if action=='start'then trace={};reset_events={}
    elseif action=='save'and trace then local stream=io.open(file,'wb');if stream then stream:write(utils.format_json({frames=trace,resets=reset_events}));stream:close()end;trace=nil end
end)
mp.register_script_message('cinema-test-marker',function(value)if settings_options.qa then test_marker=value=='yes'end end)
mp.register_event('start-file',function() reset_clock('start-file');scene=nil;has_motion=false;overlay:park();last_data='';timer:kill();schedule() end)
mp.register_event('seek',function() seeking=true;reset_clock('seek');schedule() end)
mp.register_event('playback-restart',function() seeking=false;reset_clock('playback-restart');schedule() end)
mp.register_event('file-loaded',function()reset_clock('file-loaded');schedule()end)
mp.observe_property('time-pos','number',function(_,value) if value then anchor=value;anchored_at=mp.get_time() end end)
mp.observe_property('pause','bool',function(_,value) paused=value or false;reset_clock('paused');schedule() end)
mp.observe_property('paused-for-cache','bool',function(_,value) buffering=value or false;reset_clock('buffering');schedule() end)
mp.observe_property('speed','number',function(_,value)
 if changing_speed then if clock_value then media_time(mp.get_time())end;speed=value or 1;schedule(false)
 else speed=value or 1;mp.set_property_number('user-data/cinema-transport-speed',speed);reset_clock('speed');schedule()end
end)
mp.observe_property('window-minimized','bool',function(_,value) minimized=value or false;reset_clock('minimized');schedule() end)
mp.observe_property('audio-delay','number',function(_,v)audio_delay=v or 0;reset_clock('audio-delay')end)
mp.observe_property('audio-pts','number',function(_,v)audio_anchor=v;audio_at=mp.get_time()end)
mp.observe_property('osd-dimensions','native',function(_,v)osd_dimensions=v;render()end)
mp.observe_property('display-fps','number',refresh_rate)
mp.observe_property('display-names','native',function()if vblank_clock then vblank_clock.refresh()end end)
mp.observe_property('audio-spdif','string',schedule)
mp.add_key_binding('d','cinema-toggle',toggle)
mp.add_key_binding('Ctrl+d','cinema-settings',settings)
mp.add_key_binding('Alt+LEFT','cinema-earlier',function() shift(-.5) end,{repeatable=true})
mp.add_key_binding('Alt+RIGHT','cinema-later',function() shift(.5) end,{repeatable=true})
mp.add_key_binding('Alt+0','cinema-offset-reset',function() offset=0;option('offset',0);schedule();status('弹幕偏移','已重置') end)
mp.add_key_binding('Alt+a','cinema-region',function()
    local region=mp.get_property_number('user-data/cinema-region',.5);region=region>=1 and .25 or region+.25;mp.set_property_number('user-data/cinema-region',region);option('region',region);status('弹幕区域',tostring(region*100)..'%')
end)
mp.register_event('shutdown',function() restore_speed_sync:kill();if speed_ramp then speed_ramp:kill()end;sync_release:kill();timer:kill();precision(false);if watchdog then watchdog:kill() end;if parent_handle then kernel.CloseHandle(parent_handle) end;overlay:remove() end)
overlay:prime('danmu')
