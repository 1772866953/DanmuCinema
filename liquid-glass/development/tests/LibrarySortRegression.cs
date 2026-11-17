using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DanmuCinema;
class LibrarySortRegression {
 static List<string> checks=new List<string>();
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
 static LibraryEntry Entry(string key,string name,string type,long size,int day,double bitrate,bool xml,bool anime=false){return new LibraryEntry{Key=key,Name=name,Type=type,Size=size,ModifiedUtc=new DateTime(2026,11,day,0,0,0,DateTimeKind.Utc),Bitrate=bitrate,HasXml=xml,Item=new Dictionary<string,object>{{"Id",key},{"Name",name},{"Path",@"D:\SyntheticOnly\"+name},{"Type",type},{"DanmuCinemaCategory",anime?"动漫":""}}};}
 static void Order(MediaLibrary lib,LibrarySort key,bool descending,string expected){Check(String.Join(",",lib.View("",key,descending).Select(x=>x.Key))==expected,key+" "+(descending?"降序":"升序"));}
 static int Main(string[] args){try{LibraryEntry a=Entry("a","EP2.mkv","Episode",300,3,2000000,false),b=Entry("b","EP10.mkv","Episode",100,1,12000000,false,true),c=Entry("c","Movie.mkv","Movie",200,2,6000000,true);var lib=new MediaLibrary();lib.ReplaceEntries(new[]{a,b,c});lib.Selection.Set("b",true);
 Order(lib,LibrarySort.Name,false,"a,b,c");Order(lib,LibrarySort.Name,true,"c,b,a");Order(lib,LibrarySort.Size,false,"b,c,a");Order(lib,LibrarySort.Size,true,"a,c,b");Order(lib,LibrarySort.Modified,false,"b,c,a");Order(lib,LibrarySort.Modified,true,"a,c,b");Order(lib,LibrarySort.Bitrate,false,"a,c,b");Order(lib,LibrarySort.Bitrate,true,"b,c,a");Order(lib,LibrarySort.Type,false,"b,c,a");Order(lib,LibrarySort.Type,true,"a,c,b");Order(lib,LibrarySort.Danmu,false,"a,b,c");Order(lib,LibrarySort.Danmu,true,"c,a,b");Check(lib.Selection.Contains("b")&&lib.Selection.Count==1,"排序保留选中状态");
 var missing=Entry("d","EP0.mkv","Movie",0,4,0,false);missing.Size=null;lib.ReplaceEntries(new[]{a,b,c,missing});Check(lib.View("",LibrarySort.Size,false).Last().Key=="d"&&lib.View("",LibrarySort.Size,true).Last().Key=="d","未知大小始终放在末尾");
 lib.ReplaceEntries(new[]{new LibraryEntry{Key="x",Name="季度 A",IsFolder=true,Members=new[]{a,b},Item=new Dictionary<string,object>()},new LibraryEntry{Key="y",Name="季度 B",IsFolder=true,Members=new[]{a,c},Item=new Dictionary<string,object>()},new LibraryEntry{Key="z",Name="季度 C",IsFolder=true,Members=new[]{c,c},Item=new Dictionary<string,object>()}});Order(lib,LibrarySort.Danmu,false,"x,y,z");Order(lib,LibrarySort.Danmu,true,"z,y,x");
 var report=new{passed=true,checks=checks.ToArray()};Directory.CreateDirectory(args[0]);File.WriteAllText(Path.Combine(args[0],"report.json"),Json.Write(report));Console.WriteLine(Json.Write(new{passed=true,checks=checks.Count}));return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
