using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
// Sample only a four-pixel scanline inside the owned test window. No screenshots
// or video content are retained: output is the test marker's position and time.
class MpvMotionProbe {
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Header{public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y;public uint Used,Important;}
 delegate bool EnumProc(IntPtr w,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc p,IntPtr l);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr w,out uint p);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr w,out Rect r);
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr w,ref Point p);
 [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr w);
 [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr w,IntPtr d);
 [DllImport("user32.dll")]static extern IntPtr SetProcessDpiAwarenessContext(IntPtr c);
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr d);
 [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr d,ref Header h,uint u,out IntPtr bits,IntPtr section,uint offset);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr d,IntPtr o);
 [DllImport("gdi32.dll")]static extern bool BitBlt(IntPtr dst,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint rop);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr o);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr d);
 static int Main(string[] a){try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));uint pid=uint.Parse(a[0]);IntPtr win=IntPtr.Zero;
  EnumWindows((w,l)=>{uint id;GetWindowThreadProcessId(w,out id);if(id==pid&&IsWindowVisible(w)){win=w;return false;}return true;},IntPtr.Zero);if(win==IntPtr.Zero)throw new Exception("Owned window missing");
  Rect r;GetClientRect(win,out r);Point p=new Point();ClientToScreen(win,ref p);int width=r.R,sy=p.Y+(int)Math.Round(width/1280.0*95);
  IntPtr src=GetDC(IntPtr.Zero),dst=CreateCompatibleDC(src),bits;Header h=new Header{Size=40,Width=width,Height=-4,Planes=1,Bits=32};IntPtr bmp=CreateDIBSection(src,ref h,0,out bits,IntPtr.Zero,0),old=SelectObject(dst,bmp);byte[] data=new byte[width*4*4];Stopwatch clock=Stopwatch.StartNew();double seconds=double.Parse(a[1]),next=0;
  try{while(clock.Elapsed.TotalSeconds<seconds){if(clock.Elapsed.TotalMilliseconds<next){Thread.Sleep(1);continue;}next+=2.5;
   BitBlt(dst,0,0,width,4,src,p.X,sy,0x40CC0020);Marshal.Copy(bits,data,0,data.Length);int left=-1,right=-1,count=0;
   for(int x=0;x<width;x++){int i=x*4;if(data[i]>190&&data[i+1]<80&&data[i+2]>190){if(left<0)left=x;right=x;count++;}}
   Console.WriteLine("{\"at\":"+clock.Elapsed.TotalSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"left\":"+left+",\"right\":"+right+",\"pixels\":"+count+",\"foreground\":"+(GetForegroundWindow()==win?"true":"false")+"}");
  }}finally{SelectObject(dst,old);DeleteObject(bmp);DeleteDC(dst);ReleaseDC(IntPtr.Zero,src);}return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
