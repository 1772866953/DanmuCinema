using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanmuCinema
{
    public sealed class RenameFile
    {
        public string path, name; public long size;
    }
    public sealed class RenameChange
    {
        public string source, target; public long length, writeTicks;
    }
    public sealed class RenamePreview
    {
        public string token; public RenameChange[] changes; public int count;
        public DateTime Created;
    }
    // Plans are private to the local desktop bridge. Applying a plan never
    // overwrites a file and revalidates every source before the first move.
    public sealed class MediaFileRenamer
    {
        static readonly HashSet<string> videos=new HashSet<string>(".mkv .mp4 .m4v .avi .mov .wmv .webm .flv .ts .m2ts .mts .mpg .mpeg .vob .ogv .3gp .iso".Split(' '),StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> comments=new HashSet<string>(".xml .ass .ssa .srt .vtt .smi .sup .sub .idx .ttml".Split(' '),StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,RenamePreview> plans=new Dictionary<string,RenamePreview>();
        readonly object sync=new object(); readonly Action<string,string> move;
        public MediaFileRenamer(Action<string,string> mover=null){move=mover??File.Move;}
        static string Full(string value)
        {
            if(String.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))throw new InvalidOperationException("请选择有效的本地文件。");
            return Path.GetFullPath(value);
        }
        static bool Supported(string path,bool video){return(video?videos:comments).Contains(Path.GetExtension(path));}
        static string Natural(string file){return Regex.Replace(Path.GetFileName(file),@"\d+",m=>m.Value.PadLeft(20,'0'));}
        static FileInfo Check(string file,bool video)
        {
            var info=new FileInfo(Full(file));
            if(!info.Exists||!Supported(info.FullName,video)||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("文件不可用或格式不受支持："+file);
            return info;
        }
        public static RenameFile[] Read(string directory,string[] files,bool video)
        {
            IEnumerable<string> paths=String.IsNullOrWhiteSpace(directory)?files??new string[0]:Directory.EnumerateFiles(Full(directory),"*",SearchOption.TopDirectoryOnly);
            var selected=paths.Where(x=>Supported(x,video)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).Take(5001).ToArray();
            if(selected.Length>5000)throw new InvalidOperationException("一次最多读取5000个文件。");
            return selected.OrderBy(Natural,StringComparer.OrdinalIgnoreCase).Select(x=>{var f=Check(x,video);return new RenameFile{path=f.FullName,name=f.Name,size=f.Length};}).ToArray();
        }
        public RenamePreview Preview(string[] videoFiles,string[] danmuFiles,int[] matches,bool reverse,bool placeWithVideo)
        {
            if(videoFiles==null||danmuFiles==null||matches==null||danmuFiles.Length!=matches.Length||videoFiles.Length>5000||danmuFiles.Length>5000)throw new InvalidOperationException("文件配对已改变，请重新选择。");
            var changes=new List<RenameChange>();
            for(int i=0;i<danmuFiles.Length;i++){
                int match=matches[i];if(match<0)continue;if(match>=videoFiles.Length)throw new InvalidOperationException("视频配对无效。");
                var video=Check(videoFiles[match],true);var danmu=Check(danmuFiles[i],false);
                if(reverse){Add(changes,video,Path.Combine(video.DirectoryName,Path.GetFileNameWithoutExtension(danmu.Name)+video.Extension));if(placeWithVideo)Add(changes,danmu,Path.Combine(video.DirectoryName,danmu.Name));}
                else Add(changes,danmu,Path.Combine(placeWithVideo?video.DirectoryName:danmu.DirectoryName,Path.GetFileNameWithoutExtension(video.Name)+danmu.Extension));
            }
            Validate(changes);
            var plan=new RenamePreview{token=Guid.NewGuid().ToString("N"),changes=changes.ToArray(),count=changes.Count,Created=DateTime.UtcNow};
            lock(sync){foreach(var key in plans.Where(x=>(DateTime.UtcNow-x.Value.Created).TotalMinutes>10).Select(x=>x.Key).ToArray())plans.Remove(key);if(plans.Count>=4)plans.Remove(plans.OrderBy(x=>x.Value.Created).First().Key);plans[plan.token]=plan;}
            return plan;
        }
        static void Add(List<RenameChange> changes,FileInfo file,string target)
        {
            target=Full(target);if(file.FullName==target)return;
            var existing=changes.FirstOrDefault(x=>String.Equals(x.source,file.FullName,StringComparison.OrdinalIgnoreCase));
            if(existing!=null){if(!String.Equals(existing.target,target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("同一文件不能对应多个新名称："+file.Name);return;}
            changes.Add(new RenameChange{source=file.FullName,target=target,length=file.Length,writeTicks=file.LastWriteTimeUtc.Ticks});
        }
        static void Validate(IList<RenameChange> changes)
        {
            var sources=new HashSet<string>(changes.Select(x=>x.source),StringComparer.OrdinalIgnoreCase);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var change in changes){
                var current=new FileInfo(change.source);
                if(!current.Exists||current.Length!=change.length||current.LastWriteTimeUtc.Ticks!=change.writeTicks)throw new InvalidOperationException("文件已改变，请重新预览："+Path.GetFileName(change.source));
                if(!targets.Add(change.target))throw new InvalidOperationException("多个文件将使用同一名称："+change.target);
                if(Directory.Exists(change.target)||(File.Exists(change.target)&&!sources.Contains(change.target)))throw new InvalidOperationException("目标已存在，将保留原文件："+change.target);
                if(!Directory.Exists(Path.GetDirectoryName(change.target)))throw new InvalidOperationException("目标目录不存在："+Path.GetDirectoryName(change.target));
            }
        }
        public int Apply(string token)
        {
            lock(sync){
                RenamePreview plan;if(!plans.TryGetValue(token??"",out plan)||(DateTime.UtcNow-plan.Created).TotalMinutes>10)throw new InvalidOperationException("预览已失效，请重新预览。");plans.Remove(token);Validate(plan.changes);
                var staged=new Dictionary<RenameChange,string>();var committed=new HashSet<RenameChange>();var held=new List<FileStream>();
                try{
                    // Holding read handles with delete sharing rejects files being
                    // played or edited; data remains untouched through the rename.
                    foreach(var change in plan.changes)held.Add(new FileStream(change.source,FileMode.Open,FileAccess.Read,Supported(change.source,true)?FileShare.Delete:FileShare.Read|FileShare.Delete));
                    // A file may have changed while the handles were acquired.
                    // Check again with write access excluded, before staging.
                    Validate(plan.changes);
                    foreach(var change in plan.changes){string temp=Path.Combine(Path.GetDirectoryName(change.source),".danmucinema-rename-"+Guid.NewGuid().ToString("N")+".tmp");move(change.source,temp);staged[change]=temp;}
                    foreach(var change in plan.changes){move(staged[change],change.target);committed.Add(change);}
                    return plan.count;
                }catch(Exception error){
                    var recovery=new List<string>();
                    foreach(var change in committed)try{File.Move(change.target,staged[change]);}catch{recovery.Add(change.target);}
                    foreach(var pair in staged)if(File.Exists(pair.Value))try{File.Move(pair.Value,pair.Key.source);}catch{recovery.Add(pair.Value);}
                    throw new IOException(recovery.Count==0?"未完成重命名，文件已保留原样。"+error.Message:"重命名中断，以下文件保留在临时位置："+String.Join("；",recovery),error);
                }finally{foreach(var stream in held)stream.Dispose();}
            }
        }
    }
}
