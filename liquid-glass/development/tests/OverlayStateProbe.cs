using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
class OverlayStateProbe {
 [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
 delegate bool EnumWindow(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr context);
 static int Main(string[] args){
  SetProcessDpiAwarenessContext(new IntPtr(-4));uint player=uint.Parse(args[0]);var order=new List<IntPtr>();IntPtr p=IntPtr.Zero,o=new IntPtr(long.Parse(args[1]));
  EnumWindows((h,_)=>{order.Add(h);uint id;GetWindowThreadProcessId(h,out id);Rect r;GetWindowRect(h,out r);if(id==player&&IsWindowVisible(h)&&r.Right-r.Left>100)p=h;return true;},IntPtr.Zero);
  if(p==IntPtr.Zero||o==IntPtr.Zero)return 2;Rect a,b;GetWindowRect(p,out a);GetWindowRect(o,out b);
  bool above=false;IntPtr next=GetWindow(o,2);for(int i=0;i<2000&&next!=IntPtr.Zero;i++,next=GetWindow(next,2))if(next==p){above=true;break;}
  Console.WriteLine("{\"aboveOwner\":"+(above?"true":"false")+",\"owned\":"+(GetWindow(o,4)==p?"true":"false")+",\"visible\":"+(IsWindowVisible(o)?"true":"false")+",\"minimized\":"+(IsIconic(p)?"true":"false")+",\"foregroundPlayer\":"+(GetForegroundWindow()==p?"true":"false")+",\"player\":["+a.Left+","+a.Top+","+(a.Right-a.Left)+","+(a.Bottom-a.Top)+"],\"overlay\":["+b.Left+","+b.Top+","+(b.Right-b.Left)+","+(b.Bottom-b.Top)+"]}");return 0;
 }
}
