using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
class MpvWindowProbe {
 [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
 delegate bool EnumWindow(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow f,IntPtr p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr z,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr SetProcessDpiAwarenessContext(IntPtr context);
 static IntPtr Point(int x,int y){return new IntPtr((y<<16)|(x&65535));}
 static void Mouse(IntPtr h,uint msg,int x,int y,int buttons){if(!PostMessage(h,msg,new IntPtr(buttons),Point(x,y)))throw new Exception("PostMessage failed");}
 static int Main(string[] args){try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));uint pid=uint.Parse(args[1]);IntPtr window=IntPtr.Zero;
  EnumWindows((h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==pid&&IsWindowVisible(h)){window=h;return false;}return true;},IntPtr.Zero);
  if(window==IntPtr.Zero)throw new Exception("Owned mpv test window not found");
  if(args[0]=="drag"){
   int x=int.Parse(args[2]),y=int.Parse(args[3]),tx=int.Parse(args[4]),ty=int.Parse(args[5]);Mouse(window,0x200,x,y,0);Thread.Sleep(60);Mouse(window,0x201,x,y,1);Thread.Sleep(60);
   for(int i=1;i<=16;i++){Mouse(window,0x200,x+(tx-x)*i/16,y+(ty-y)*i/16,1);Thread.Sleep(20);}Mouse(window,0x202,tx,ty,0);Thread.Sleep(120);
  }else if(args[0]=="burst"){
   int x=int.Parse(args[2]),y=int.Parse(args[3]),count=int.Parse(args[4]),gap=int.Parse(args[5]);bool doubles=args.Length>6&&args[6]=="double";Mouse(window,0x200,x,y,0);Thread.Sleep(30);
   for(int i=0;i<count;i++){Mouse(window,doubles&&i%2==1?0x203u:0x201u,x,y,1);Thread.Sleep(12);Mouse(window,0x202,x,y,0);Thread.Sleep(gap);}
  }else if(args[0]=="leave"){
   PostMessage(window,0x2A3,IntPtr.Zero,IntPtr.Zero);
  }else if(args[0]=="rawmove"||args[0]=="heldmove"||args[0]=="down"||args[0]=="up"){
   Mouse(window,args[0]=="down"?0x201u:args[0]=="up"?0x202u:0x200u,int.Parse(args[2]),int.Parse(args[3]),args[0]=="down"||args[0]=="heldmove"?1:0);
  }else if(args[0]=="click"){
   int x=int.Parse(args[2]),y=int.Parse(args[3]);bool right=args.Length>4&&args[4]=="right";Mouse(window,0x200,x,y,0);Thread.Sleep(40);Mouse(window,right?0x204u:0x201u,x,y,right?2:1);Thread.Sleep(40);Mouse(window,right?0x205u:0x202u,x,y,0);Thread.Sleep(120);
  }else if(args[0]=="move"){Mouse(window,0x200,int.Parse(args[2]),int.Parse(args[3]),0);Thread.Sleep(150);}
  else if(args[0]=="activate"){SetForegroundWindow(window);Thread.Sleep(180);}
  else if(args[0]=="focus"||args[0]=="blur"){PostMessage(window,6,new IntPtr(args[0]=="focus"?1:0),IntPtr.Zero);PostMessage(window,args[0]=="focus"?7u:8u,IntPtr.Zero,IntPtr.Zero);Thread.Sleep(150);}
  else if(args[0]=="close"){PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);}
  else if(args[0]=="maximize"||args[0]=="restore"||args[0]=="minimize"){ShowWindow(window,args[0]=="maximize"?3:args[0]=="minimize"?6:9);Thread.Sleep(250);}
  else if(args[0]=="resize"){
   Rect outer,client;GetWindowRect(window,out outer);GetClientRect(window,out client);
   if(!SetWindowPos(window,IntPtr.Zero,outer.Left,outer.Top,int.Parse(args[2])+outer.Right-outer.Left-client.Right,int.Parse(args[3])+outer.Bottom-outer.Top-client.Bottom,0x14))throw new Exception("Resize failed");Thread.Sleep(200);
  }
  Rect r,c;GetWindowRect(window,out r);GetClientRect(window,out c);
  Console.WriteLine("{\"x\":"+r.Left+",\"y\":"+r.Top+",\"width\":"+(r.Right-r.Left)+",\"height\":"+(r.Bottom-r.Top)+",\"clientWidth\":"+c.Right+",\"clientHeight\":"+c.Bottom+"}");return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
