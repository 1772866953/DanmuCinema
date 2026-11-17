using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
class ProcessPowerProbe {
 [StructLayout(LayoutKind.Sequential)] struct Power {public uint Version,ControlMask,StateMask;}
 [StructLayout(LayoutKind.Sequential)] struct Basic {public IntPtr Reserved1,Peb,Reserved2,Reserved3,Pid,ParentPid;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint rights,bool inherit,int pid);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessInformation(IntPtr h,int type,ref Power p,uint size);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetProcessInformation(IntPtr h,int type,ref Power p,uint size);
 [DllImport("ntdll.dll")]static extern int NtQueryInformationProcess(IntPtr h,int type,out Basic p,int size,IntPtr length);
 static bool Owned(int pid,int root){for(int depth=0;depth<12&&pid>0;depth++){if(pid==root)return true;var h=OpenProcess(0x400,false,pid);if(h==IntPtr.Zero)return false;Basic p;int result=NtQueryInformationProcess(h,0,out p,Marshal.SizeOf(typeof(Basic)),IntPtr.Zero);CloseHandle(h);if(result!=0)return false;pid=p.ParentPid.ToInt32();}return false;}
 static int Main(string[] args){try{
  bool apply=args[0]=="apply";int root=int.Parse(args[1]);var results=new List<string>();
  for(int i=2;i<args.Length;i++){
   int pid=int.Parse(args[i]);if(!Owned(pid,root))throw new Exception("Target is not owned by the QA application");
   var h=OpenProcess(apply?0x1200u:0x1000u,false,pid);if(h==IntPtr.Zero)throw new Exception("Cannot open QA process: "+Marshal.GetLastWin32Error());
   try{var power=new Power{Version=1};if(!GetProcessInformation(h,4,ref power,(uint)Marshal.SizeOf(typeof(Power))))throw new Exception("Cannot read process power state: "+Marshal.GetLastWin32Error());uint control=power.ControlMask,state=power.StateMask;
    if(apply){power.ControlMask|=5;power.StateMask&=~5u;if(!SetProcessInformation(h,4,ref power,(uint)Marshal.SizeOf(typeof(Power))))throw new Exception("Cannot set process power state: "+Marshal.GetLastWin32Error());}
    results.Add("{\"pid\":"+pid+",\"previousControl\":"+control+",\"previousState\":"+state+",\"control\":"+power.ControlMask+",\"state\":"+power.StateMask+"}");
   }finally{CloseHandle(h);}
  }
  Console.WriteLine("["+String.Join(",",results)+"]");return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}
