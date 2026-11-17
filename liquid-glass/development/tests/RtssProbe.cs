using System;
using System.IO.MemoryMappedFiles;
using System.Threading;
class RtssProbe {
 static int Main(string[] args){try{
  uint pid=UInt32.Parse(args[0]);int seconds=Int32.Parse(args[1]);
  using(var map=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.Read))using(var view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read)){
   uint version=view.ReadUInt32(4),size=view.ReadUInt32(8),offset=view.ReadUInt32(12),count=view.ReadUInt32(16);
   if(version<0x20000||size<284||count>4096)throw new Exception("Unexpected RTSS layout");
   var end=DateTime.UtcNow.AddSeconds(seconds);
   while(DateTime.UtcNow<end){bool found=false;for(uint i=0;i<count;i++){long p=offset+(long)i*size;if(view.ReadUInt32(p)!=pid)continue;uint a=view.ReadUInt32(p+268),b=view.ReadUInt32(p+272),frames=view.ReadUInt32(p+276),micros=view.ReadUInt32(p+280);Console.WriteLine("{\"pid\":"+pid+",\"fps\":"+(b>a?1000.0*frames/(b-a):0).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"frameMs\":"+(micros/1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");found=true;break;}if(!found)Console.WriteLine("{\"pid\":"+pid+",\"missing\":true}");Thread.Sleep(250);}
  }return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
