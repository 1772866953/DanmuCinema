using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace DanmuCinema {
    public static class PlaybackQueue {
        static readonly HashSet<string> Extensions = new HashSet<string>(new[]{".mkv",".mp4",".avi",".mov",".wmv",".m4v",".ts",".m2ts",".mts",".flv",".webm",".mpg",".mpeg",".vob",".iso",".mxf",".rmvb",".rm",".asf",".3gp",".m2v",".ogv"},StringComparer.OrdinalIgnoreCase);
        public static LibraryEntry[] Resolve(LibraryEntry[] all, string[] keys, bool natural, bool siblings) {
            var selected=keys.Select(k=>all.FirstOrDefault(x=>x.Key==k)).ToArray();
            if(selected.Length==0||selected.Any(x=>x==null||x.IsFolder||!File.Exists(Json.Text(x.Item,"Path"))))throw new InvalidOperationException("影片已改变或文件不可用，请刷新媒体库。");
            if(siblings&&selected.Length==1){
                var folder=MediaLibrary.DirectoryOf(selected[0]);
                var entries=all.Where(x=>!x.IsFolder&&String.Equals(MediaLibrary.DirectoryOf(x),folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(Json.Text(x.Item,"Path"))).GroupBy(x=>Path.GetFullPath(Json.Text(x.Item,"Path")),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
                entries[Path.GetFullPath(Json.Text(selected[0].Item,"Path"))]=selected[0];
                // Resolve from the actual directory too: a cold/partial index must
                // not turn the first playback into a one-entry queue. Read-only,
                // same-directory only, and anchored to an already validated item.
                try{foreach(var file in Directory.EnumerateFiles(folder).Where(p=>Extensions.Contains(Path.GetExtension(p)))){
                    if(entries.Count>=5000)break;
                    if(!entries.ContainsKey(file))entries[file]=new LibraryEntry{Key="path:"+file,Name=Path.GetFileName(file),Item=new Dictionary<string,object>{{"Path",file},{"Name",Path.GetFileNameWithoutExtension(file)},{"Type",selected[0].Type??"Video"}}};
                }
                }catch(UnauthorizedAccessException){}catch(IOException){}
                selected=entries.Values.ToArray();natural=true;
            }
            return natural?selected.OrderBy(MediaLibrary.DirectoryOf,NaturalNames.Instance).ThenBy(x=>x.Name,NaturalNames.Instance).ToArray():selected;
        }
    }
}
