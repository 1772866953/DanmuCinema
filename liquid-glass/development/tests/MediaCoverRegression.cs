using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;
class MediaCoverRegression {
 static List<string> checks=new List<string>();
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
 static LibraryEntry Entry(string name,int season,int episode){return new LibraryEntry{Key=name,Name=name,Item=new Dictionary<string,object>{{"ParentIndexNumber",season},{"IndexNumber",episode}}};}
 static string Hash(string file){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file)));}
 static async Task Run(string[] args){
  Paths.Root=Path.Combine(args[0],"isolated");Directory.CreateDirectory(Paths.Root);
  var ep10=Entry("EP10.mkv",1,10);var ep2=Entry("EP2.mkv",1,2);var ep1=Entry("EP1.mkv",1,1);var s2=Entry("EP1-S2.mkv",2,1);
  Check(MediaCovers.First(new[]{ep10,ep2,ep1,s2})==ep1,"跨目录候选按季度与集数选择第一集");
  Check(MediaCovers.First(new[]{Entry("SP.mkv",0,1),ep1,s2})==ep1,"特别篇不抢占正片第一集的封面");
  ep1.Item.Clear();ep2.Item.Clear();ep10.Item.Clear();Check(MediaCovers.First(new[]{ep10,ep2,ep1})==ep1,"缺少集数元数据时自然排序选取首集");
  var item=new Dictionary<string,object>{{"Id","episode"},{"SeriesId","series"},{"SeriesPrimaryImageTag","series-tag"},{"ImageTags",new Dictionary<string,object>{{"Primary","episode-tag"}}}};
  var requests=new List<string>();int frames=0;byte[] series={1,2},episodeImage={3,4},frame={5,6};
  Func<Task<byte[]>> fallback=()=>{frames++;return Task.FromResult(frame);};
  Func<string,string,Task<byte[]>> poster=(id,tag)=>{requests.Add(id+":"+tag);return Task.FromResult(id=="series"?series:episodeImage);};
  Check((await MediaCovers.Resolve(item,true,poster,fallback))==series&&requests.SequenceEqual(new[]{"series:series-tag"})&&frames==0,"季度优先采用 Jellyfin 作品海报");requests.Clear();
  Check((await MediaCovers.Resolve(item,false,poster,fallback))==episodeImage&&requests.SequenceEqual(new[]{"episode:episode-tag"}),"分集只采用本集图片，不挪用季度海报");requests.Clear();
  Check((await MediaCovers.Resolve(item,true,(id,tag)=>{requests.Add(id);return Task.FromResult(id=="series"?null:episodeImage);},fallback))==episodeImage&&requests.SequenceEqual(new[]{"series","episode"}),"季度海报不存在时回退首集图片");
  Check((await MediaCovers.Resolve(item,true,(id,tag)=>{throw new IOException("模拟图片服务不可用");},fallback))==frame&&frames==1,"图片服务失败时回退本地影片帧");
  Check((await MediaCovers.Resolve(item,false,(id,tag)=>Task.FromResult(new byte[4*1024*1024+1]),fallback))==frame,"拒绝异常大图片并回退影片帧");
  var cts=new CancellationTokenSource();cts.Cancel();bool canceled=false;try{await MediaCovers.Resolve(item,true,(id,tag)=>{throw new OperationCanceledException(cts.Token);},fallback);}catch(OperationCanceledException){canceled=true;}Check(canceled,"取消请求不触发后续回退任务");
  typeof(Paths).GetField("RuntimeOverride",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(args[1],"runtime","jellyfin"));
  string video=Path.Combine(Paths.Root,"first episode.mkv");File.Copy(args[2],video,true);string before=Hash(video);
  var image=await MediaCovers.Frame(video,1,CancellationToken.None);Check(image!=null&&image.Length>2&&image[0]==255&&image[1]==216,"内置 FFmpeg 可读取带空格路径并返回 JPEG 封面");Check(Hash(video)==before&&Directory.GetFiles(Paths.Root).Length==1,"封面生成不修改影片，也不在媒体目录创建文件");
  var settings=(AppSettings)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AppSettings));settings.CacheRetentionMonths=3;
  var cache=new DandanApiCache(settings);string key=DandanApiCache.Key("synthetic-cover");cache.Write(key,"poster","fixture",Convert.ToBase64String(image),"fixture");Check(cache.Read(key)==Convert.ToBase64String(image),"封面可从本地持久缓存复用");cache.Remove(cache.Entries().Where(x=>x.Kind=="poster").Select(x=>x.Key));Check(cache.Read(key)==null&&Hash(video)==before,"清理封面缓存仅删除缓存，影片保持原样");
 }
 static int Main(string[] args){try{Run(args).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(args[0],"report.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(new{passed=true,checks=checks.Count}));return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
