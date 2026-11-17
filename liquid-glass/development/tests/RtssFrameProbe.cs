using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;
// Read-only v2.5+ RTSS frame-time history, restricted to the owned test PID.
class RtssFrameProbe {
 static int Main(string[] args){try{uint pid=UInt32.Parse(args[0]);double seconds=Double.Parse(args[1]);
  using(var map=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.Read))using(var view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read)){
   uint version=view.ReadUInt32(4),size=view.ReadUInt32(8),offset=view.ReadUInt32(12),count=view.ReadUInt32(16);if(version<0x20005||size<5028||count>4096)throw new Exception("Unsupported RTSS history layout");
   long found=-1;uint previous=0;bool initialized=false;Stopwatch clock=Stopwatch.StartNew();
   while(clock.Elapsed.TotalSeconds<seconds){if(found<0){for(uint i=0;i<count;i++){long p=offset+(long)i*size;if(view.ReadUInt32(p)==pid){found=p;break;}}}
    if(found>=0){uint position=view.ReadUInt32(found+5020);if(!initialized){previous=position;initialized=true;}uint length=(position-previous)&1023;
     if(length>0){string values="";for(uint i=0;i<length;i++){if(i>0)values+=",";values+=view.ReadUInt32(found+924+4*((previous+i)&1023));}Console.WriteLine("{\"at\":"+clock.Elapsed.TotalSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+",\"position\":"+position+",\"micros\":["+values+"]}");previous=position;}
    }Thread.Sleep(100);
   }
  }return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
