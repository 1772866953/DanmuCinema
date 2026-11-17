using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DanmuCinema;
class FileRenameRegression
{
    static readonly List<string> checks=new List<string>();static string root;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(IOException){rejected=true;}Check(rejected,name);}
    static string FileIn(string directory,string name,string body){directory=Path.Combine(root,directory);Directory.CreateDirectory(directory);string p=Path.Combine(directory,name);File.WriteAllText(p,body);return p;}
    static int Main(string[] args){try{
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var rename=new MediaFileRenamer();string v=FileIn("videos","第 01 集.MKV","VIDEO"),d=FileIn("comments","原弹幕.XML","DANMU");FileIn("videos/child","第 02 集.mkv","NESTED");FileIn("comments/child","第二集.xml","NESTED COMMENT");FileIn("comments","说明.txt","TEXT");
        Check(MediaFileRenamer.Read(Path.GetDirectoryName(v),null,true).Length==1&&MediaFileRenamer.Read(Path.GetDirectoryName(d),null,false).Length==1,"目录选择只读取根层视频和弹幕，忽略子目录及说明文件");
        var tenth=FileIn("order","第10集.mkv","TEN");FileIn("order","第2集.mkv","TWO");FileIn("order","第1集.mkv","ONE");
        Check(String.Join("|",MediaFileRenamer.Read(Path.GetDirectoryName(tenth),null,true).Select(x=>x.name))=="第1集.mkv|第2集.mkv|第10集.mkv","目录文件按集数自然排序，避免第10集排在第2集之前");
        var plan=rename.Preview(new[]{v},new[]{d},new[]{0},false,false);string target=Path.Combine(Path.GetDirectoryName(d),"第 01 集.XML");Check(plan.changes.Single().target==target,"弹幕采用视频名称并保留扩展名和原目录");Check(rename.Apply(plan.token)==1&&File.ReadAllText(target)=="DANMU"&&!File.Exists(d)&&File.ReadAllText(v)=="VIDEO","执行弹幕重命名不修改视频或弹幕内容");Reject(()=>rename.Apply(plan.token),"已执行的预览不能重复使用");
        plan=rename.Preview(new[]{v},new[]{target},new[]{0},false,false);Check(plan.count==0,"同名文件无需重复处理");
        d=FileIn("reverse","新剧集.srt","SUBTITLE");plan=rename.Preview(new[]{v},new[]{d},new[]{0},true,false);string newVideo=Path.Combine(Path.GetDirectoryName(v),"新剧集.MKV");rename.Apply(plan.token);Check(File.ReadAllText(newVideo)=="VIDEO"&&File.ReadAllText(d)=="SUBTITLE"&&!File.Exists(v),"反向重命名支持字幕文件名跟随并保持视频内容原样");
        plan=rename.Preview(new[]{newVideo},new[]{d},new[]{0},false,true);rename.Apply(plan.token);Check(File.Exists(Path.Combine(Path.GetDirectoryName(newVideo),"新剧集.srt"))&&!File.Exists(d),"可选将弹幕或字幕放在对应视频目录");
        var a=FileIn("cycle","A.xml","A CONTENT");var b=FileIn("cycle","B.xml","B CONTENT");var va=FileIn("cycle-video","A.mkv","A VIDEO");var vb=FileIn("cycle-video","B.mkv","B VIDEO");
        plan=rename.Preview(new[]{vb,va},new[]{a,b},new[]{0,1},false,false);rename.Apply(plan.token);Check(File.ReadAllText(a)=="B CONTENT"&&File.ReadAllText(b)=="A CONTENT","交换文件名采用临时中转，不覆盖任何原文件");
        var collision=FileIn("collision","A.xml","KEEP A");var extra=FileIn("collision","B.xml","KEEP B");Reject(()=>rename.Preview(new[]{vb},new[]{collision},new[]{0},false,false),"目标文件已存在时拒绝整个计划");Check(File.ReadAllText(collision)=="KEEP A"&&File.ReadAllText(extra)=="KEEP B","冲突时所有源文件保持原样");
        var one=FileIn("duplicates","one.xml","ONE");var two=FileIn("duplicates","two.xml","TWO");Reject(()=>rename.Preview(new[]{va},new[]{one,two},new[]{0,0},false,false),"多个文件指向同一目标时拒绝执行");
        plan=rename.Preview(new[]{va},new[]{one},new[]{0},false,false);File.AppendAllText(one,"CHANGED");Reject(()=>rename.Apply(plan.token),"预览后文件变化时停止执行");Check(File.Exists(one),"失效计划不移动源文件");
        int moves=0;var failing=new MediaFileRenamer((source,destination)=>{if(++moves==4)throw new IOException("simulated failure");File.Move(source,destination);});plan=failing.Preview(new[]{vb,va},new[]{a,b},new[]{0,1},false,false);Reject(()=>failing.Apply(plan.token),"批量中途失败可回滚");Check(File.ReadAllText(a)=="B CONTENT"&&File.ReadAllText(b)=="A CONTENT"&&!Directory.EnumerateFiles(Path.GetDirectoryName(a),"*.tmp").Any(),"回滚恢复全部原名和内容且无临时文件残留");
        var locked=FileIn("locked","locked.mkv","LOCKED VIDEO");var sub=FileIn("locked-sub","new.xml","COMMENT");plan=rename.Preview(new[]{locked},new[]{sub},new[]{0},true,false);using(var handle=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.Read)){Reject(()=>rename.Apply(plan.token),"正在使用的视频拒绝重命名");}Check(File.ReadAllText(locked)=="LOCKED VIDEO"&&File.ReadAllText(sub)=="COMMENT","占用检查发生在首次移动之前");
        File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks}));Console.WriteLine(Json.Write(checks));return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
