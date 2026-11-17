-- Compact context menus. Layout is in display-DPI units, never video-height
-- units. All panes use the existing opacity/hover timing and vector text.
local mp=require 'mp'
local M={}
function M.cancel_hover(menu)if menu and menu.hover_timer then menu.hover_timer:kill();menu.hover_timer=nil end;if menu then menu.hover=nil end end
local function selectable(items,index,direction)
 local n=#items;if n==0 then return 1 end
 index=math.max(1,math.min(n,index))
 while items[index]and items[index].separator do index=index+(direction or 1);if index<1 or index>n then return selectable(items,direction and direction<0 and n or 1,direction)end end
 return index
end
local function items_at(menu,depth)
 local items=menu.items
 for d=1,depth-1 do local item=items[menu.path[d]or 0];if not item or not item.children then return nil end;items=item.children end
 return items
end
local function choose(menu,depth,index)
 menu.selection[depth]=index;menu.path[depth]=items_at(menu,depth)[index].children and index or nil
 for d=depth+1,12 do menu.path[d]=nil;menu.selection[d]=nil end
 menu.depth=depth
 if menu.path[depth]then menu.selection[depth+1]=selectable(items_at(menu,depth+1),1,1);menu.opened[depth+1]=mp.get_time()end
end
function M.new(items,x,y)return{items=items,path={},selection={selectable(items,1,1)},opened={[1]=mp.get_time()},depth=1,anchor={x=x,y=y},panes={}}end
function M.navigate(menu,n)
 M.cancel_hover(menu)
 local items=items_at(menu,menu.depth);local i=menu.selection[menu.depth]or 1
 if n< -999 then i=1 elseif n>999 then i=#items else i=math.max(1,math.min(#items,i+n))end
 menu.selection[menu.depth]=selectable(items,i,n<0 and -1 or 1)
 menu.path[menu.depth]=nil
end
function M.right(menu)
 M.cancel_hover(menu)
 local items=items_at(menu,menu.depth);local i=menu.selection[menu.depth]or 1
 if items[i]and items[i].children then choose(menu,menu.depth,i);menu.depth=menu.depth+1;return true end
 return false
end
function M.left(menu)
 M.cancel_hover(menu)
 if menu.depth<=1 then return false end
 menu.path[menu.depth-1]=nil;menu.depth=menu.depth-1;return true
end
function M.current(menu)local items=items_at(menu,menu.depth);return items and items[menu.selection[menu.depth]or 1]end
function M.contains(menu,x,y)for _,p in ipairs(menu.panes)do if x>=p.x and x<=p.x+p.w and y>=p.y and y<=p.y+p.h then return true end end;return false end
function M.move(menu,x,y,wake)
 for pindex=#menu.panes,1,-1 do local pane=menu.panes[pindex]
  if x>=pane.x and x<=pane.x+pane.w and y>=pane.y and y<=pane.y+pane.h then
   for i,r in ipairs(pane.rows)do if not r.item.separator and y>=r.y and y<r.y+r.h then
    menu.selection[pane.depth]=i;menu.depth=pane.depth
    local key=pane.depth..':'..i
    if menu.hover~=key then
     menu.hover=key;if menu.hover_timer then menu.hover_timer:kill()end
     local depth=pane.depth
     menu.hover_timer=mp.add_timeout(.07,function()if menu.hover==key then choose(menu,depth,i);wake()end end)
    end
    return
   end end
  end
 end
end
function M.draw(a,menu,ctx)
 menu.animating=false
 local api=ctx.api;local bw=math.min(276,ctx.w-16);local font=16
 local count,separators=0,0;for _,item in ipairs(menu.items)do if item.separator then separators=separators+1 else count=count+1 end end
 local rh=math.min(32,math.max(font+2,(ctx.h-60-separators*8)/math.max(1,count)))
 local panes={};local items=menu.items;local x,y=menu.anchor.x,menu.anchor.y
 local union
 for depth=1,10 do
  bw=math.min(depth==1 and 220 or 264,ctx.w-16)
  local header=depth==1 and 28 or 0;local total=16+header
  for _,item in ipairs(items)do total=total+(item.separator and 8 or rh)end
  x=math.max(8,math.min(ctx.w-bw-8,x));y=math.max(8,math.min(ctx.h-total-8,y))
  local p={x=x,y=y,w=bw,h=total,depth=depth,rows={}};panes[#panes+1]=p
  local fade=depth==1 and 1 or math.max(0,math.min(1,(mp.get_time()-(menu.opened[depth]or 0))/.16))
  if fade<1 then menu.animating=true;ctx.wake()end
  local alpha=math.floor(255-(255-ctx.alpha)*fade)
  api.box(a,x,y,bw,total,12,'202D27',math.min(255,alpha+10))
  if depth==1 then
   api.text(a,x+14,y+15,'播放菜单',12,'AFC5B8',4,alpha)
   api.feedback(a,'menu-close',x+bw-33,y+5,24,24,alpha)
   api.text(a,x+bw-21,y+17,'×',18,'CFDDD6',5,alpha)
   api.hit(x+bw-34,y+3,28,28,ctx.close,nil,'close')
  end
  local ry=y+8+header
  for i,item in ipairs(items)do
   local height=item.separator and 8 or rh;p.rows[i]={y=ry,h=height,item=item}
   if item.separator then api.box(a,x+13,ry+3,bw-26,1,.5,'52685B',math.min(255,alpha+90))
   else
    local selected=menu.selection[depth]==i
    if selected then api.box(a,x+6,ry,bw-12,rh-2,6,'A4C6A2',math.min(255,alpha+12))end
    api.feedback(a,'menu-'..depth..'-'..i,x+6,ry,bw-12,rh-2,alpha)
    local colour=item.disabled and '76877E'or selected and '15291F'or 'E5EEE8'
    api.text(a,x+25,ry+rh/2-1,item.title,font,colour,4,alpha)
    if item.checked then api.text(a,x+13,ry+rh/2-1,'✓',11,colour,5,alpha)end
    if item.children then api.text(a,x+bw-17,ry+rh/2-1,'›',18,colour,5,alpha)
    elseif item.shortcut then api.text(a,x+bw-15,ry+rh/2-1,item.shortcut,11,selected and '365A43'or '8EA99A',6,alpha)end
    api.hit(x+6,ry,bw-12,rh-2,function()if item.disabled then return end;menu.depth=depth;menu.selection[depth]=i;if item.children then choose(menu,depth,i);menu.depth=depth+1;ctx.wake()else ctx.invoke(item)end end,nil,'menu:'..depth..':'..i)
   end
   ry=ry+height
  end
  if not union then union={x=x,y=y,w=bw,h=total}else local r=math.max(union.x+union.w,x+bw);local b=math.max(union.y+union.h,y+total);union.x=math.min(union.x,x);union.y=math.min(union.y,y);union.w=r-union.x;union.h=b-union.y end
  local index=menu.path[depth];local child=index and items[index]
  if not child or not child.children then break end
  local proposed=x+bw+5;x=proposed+264<=ctx.w-8 and proposed or x-264-5
  y=p.rows[index].y-8;items=child.children
 end
 menu.panes=panes
 return union
end
M.items_at=items_at
return M
