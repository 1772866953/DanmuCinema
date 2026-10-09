using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DanmuCinema;
using DanmuCinema.Desktop;

public static class EpisodicBatchTests
{
    static readonly List<string> Report = new List<string>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Report.Add("PASS: " + message); }
    static Dictionary<string, object> Movie(string folder, string filename, string name = null)
    { return new Dictionary<string, object> { { "Id", filename }, { "Type", "Movie" }, { "Path", Path.Combine(folder, filename) }, { "Name", name ?? Path.GetFileNameWithoutExtension(filename) } }; }
    static bool Rejected(params Dictionary<string, object>[] items)
    { try { MediaLibrary.RequireSameSeason(items); return false; } catch (InvalidOperationException) { return true; } }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]); Paths.Root = root;
            string folder = Path.Combine(root, "videos", "你遭难了吗？"); Directory.CreateDirectory(folder);
            var videos = Enumerable.Range(1, 12).Select(n => Movie(folder, "[DMG&VCB-Studio] Sounan Desuka [" + n.ToString("D2") + "]" + (n == 3 ? "[1080p][x264_flac]" : "[Ma10p_1080p][x265_flac]") + ".mkv", n == 3 ? "Sounan Desuka" : "Sounan Desuka [Ma10p")).ToArray();
            foreach (var video in videos) File.WriteAllText(Json.Text(video,"Path"), "synthetic video fixture");
            var library = new MediaLibrary(); library.Replace(videos);
            var folders = library.Browse(null, "", LibrarySort.Name, false, Path.Combine(root, "videos"));
            Check(folders.Length == 1 && folders[0].Members.Length == 12, "Screenshot releases remain in one folder");
            foreach (string key in folders[0].SelectionKeys) library.Selection.Set(key, true);
            Check(library.SelectedItems.Length == 12, "Selecting one folder selects exactly its 12 episodes");
            MediaLibrary.RequireSameSeason(library.SelectedItems);
            Check(videos.All(v => SmartMatching.SameSeason(videos[0], v)), "Movie-labelled episodes accepted as the same season");
            Check(library.Browse(folder,"",LibrarySort.Name,false).Length == 12, "Opening folder shows all 12 selectable files");
            var remote = Enumerable.Range(1,12).Select(n => new Dictionary<string,object>{{"Number",n.ToString("D2")},{"Title","第 "+n+" 集"}}).ToArray();
            var plan = BatchMatching.Plan(library.SelectedItems, remote);
            Check(plan.Count == 12 && plan.All(p => p.Selected && p.Remote != null) && plan.Select(p=>p.Number).SequenceEqual(Enumerable.Range(1,12)), "All 12 episodes matched once and selected for batch preview");
            Check(videos.All(v => SmartMatching.IsEpisodic(v) && !SmartMatching.IsStandaloneMovie(v)), "Episode filenames override Movie type for batch availability");
            Check(MediaPresentation.Title(videos[0]) == "Sounan Desuka · 第 01 集" && MediaPresentation.Title(videos[2]) == "Sounan Desuka · 第 03 集", "Broken release metadata tags removed from displayed titles");
            Check(MediaNames.SearchTitle(videos[0]) == "你遭难了吗？", "Search still uses local anime folder title");
            var film = Movie(folder,"Standalone Film (2025) [1080p].mkv"); film["IndexNumber"] = 37;
            Check(SmartMatching.IsStandaloneMovie(film) && Rejected(videos[0],film), "Real movie with unrelated scraper index is not batched");
            Check(Rejected(videos[0],Movie(folder,"Other Anime [02][1080p].mkv")), "Different anime in one folder rejected");
            var different = Movie(folder,"[AnotherGroup] Sounan Desuka [02][1080p].mkv"); different["ParentIndexNumber"] = 2; videos[0]["ParentIndexNumber"] = 1;
            Check(Rejected(videos[0],different), "Explicitly different seasons rejected");
            videos[0].Remove("ParentIndexNumber");
            var s1 = Movie(folder,"Show S01E01 [1080p].mkv","Show"); var s2 = Movie(folder,"Show S02E02 [1080p].mkv","Show");
            Check(SmartMatching.Season(s1)==1 && SmartMatching.Season(s2)==2 && Rejected(s1,s2), "Season conflicts detected in original filenames even if metadata strips them");
            Check(SmartMatching.SameSeason(Movie(folder,"Show E01.mkv"),Movie(folder,"Show E02.mkv")) && SmartMatching.SameSeason(Movie(folder,"Show.01.mkv"),Movie(folder,"Show.02.mkv")), "Labelled and dotted episode formats group correctly");
            Check(Rejected(videos[0],Movie(Path.Combine(root,"other-folder"),"Sounan Desuka [02].mkv")), "Unrelated folder not merged without verified series metadata");
            var duplicate = BatchMatching.Plan(videos.Concat(new[]{Movie(folder,"[AnotherGroup] Sounan Desuka [01][1080p].mkv")}),remote);
            Check(duplicate.Where(p=>p.Number==1).All(p=>!p.Selected), "Duplicate releases still require manual choice");
            Check(videos.All(v=>File.ReadAllText(Json.Text(v,"Path"))=="synthetic video fixture"), "Matching never modifies video content");
            string work = Path.Combine(root,"videos","yn战记S2"), release = Path.Combine(work,"[LoliHouse][01-12][1080P][简繁内封字幕]");
            Directory.CreateDirectory(release);
            var nested = Enumerable.Range(1,12).Select(n=>Movie(release,"[LoliHouse] Youjo Senki ["+n.ToString("D2")+"][1080P].mkv")).ToArray();
            foreach(var item in nested) File.WriteAllText(Json.Text(item,"Path"),"nested fixture");
            library.Replace(nested); var upper = library.Browse(null,"",LibrarySort.Name,false,Path.Combine(root,"videos"));
            foreach(var key in upper[0].SelectionKeys) library.Selection.Set(key,true);
            Check(upper.Length==1 && upper[0].FolderPath==work && library.SelectedItems.Length==12,"Upper folder recursively selects all 12 files in release subdirectory");
            MediaLibrary.RequireSameSeason(library.SelectedItems);
            Check(nested.All(x=>MediaNames.SearchTitle(x)=="yn战记" && SmartMatching.Season(x)==2),"Release-only subdirectory skipped for anime search title while parent season is retained");
            Check(BatchMatching.Plan(library.SelectedItems,remote).All(p=>p.Selected),"Nested folder selection produces 12 checked batch matches");
            var sibling=Movie(Path.Combine(work,"[AnotherGroup][1080p]"),"Youjo Senki [03].mkv");
            Check(SmartMatching.SameSeason(nested[0],sibling),"Same anime in technical sibling release directories can share season source");
            Check(Rejected(nested[0],Movie(Path.Combine(work,"[AnotherGroup][1080p]"),"Other Anime [03].mkv")),"Technical siblings with different filename titles still rejected");
            Check(Rejected(nested[0],Movie(Path.Combine(root,"videos","yn战记S1","[LoliHouse][1080p]"),"Youjo Senki [03].mkv")),"Nested releases of another season not merged");
            Check(library.Browse(work,"",LibrarySort.Name,false).Single().Members.Length==12 && library.Browse(release,"",LibrarySort.Name,false).Length==12,"Browsing preserves nested folder hierarchy and complete file list");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.InstallTheme(app);
            var settings = new AppSettings { MediaFolder=Path.Combine(root,"videos"), EnableDandan=false, EnableExistingDanmu=false, EnableAnimeko=false, EnableBahamut=false };
            using (var controller = new DesktopController(app, settings, false))
            {
                controller.Library.Replace(videos);
                var state = new MatchState { Item=videos[0], Selected=videos, Scope=DanmuMatchScope.Selection, Keyword="你遭难了吗？", AnimeOnly=true,
                    Sources=new object[]{new Dictionary<string,object>{{"Name","你遭难了吗？"},{"Site","fixture"}}}, SourceIndex=0, Episodes=remote.Cast<object>().ToArray() };
                using (var view = new MatchView(controller,state,false,()=>{}))
                {
                    var scopes = (TabControl)typeof(MatchView).GetField("scopes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                    Check(scopes.Items.Count==3 && (DanmuMatchScope)((TabItem)scopes.SelectedItem).Tag==DanmuMatchScope.Selection, "WPF enables whole-season and selected-12 tabs for Movie-labelled episodes");
                    var preview = typeof(MatchView).GetMethod("DownloadGroup",BindingFlags.Instance|BindingFlags.NonPublic);
                    ((Task)preview.Invoke(view,new object[]{true})).GetAwaiter().GetResult();
                    Check(controller.BatchPlan.Count==12 && controller.BatchPlan.All(p=>p.Selected), "WPF selected preview produces 12 checked episodes");
                    ((Task)preview.Invoke(view,new object[]{false})).GetAwaiter().GetResult();
                    Check(controller.BatchPlan.Count==12 && controller.BatchPlan.All(p=>p.Selected), "WPF whole-season preview produces 12 checked episodes");
                    Check(new LibraryRow(controller.Library.Entries[0],controller.Library.Selection).Type=="剧集", "WPF displays mislabelled release as an episode");
                }
                using (var view = new MatchView(controller,new MatchState { Keyword="你遭难了吗？",Selected=new Dictionary<string,object>[0] },false,()=>{}))
                {
                    var local = (ComboBox)typeof(MatchView).GetField("localTarget",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                    Check(local.Items.Count==1, "Direct search includes Movie-labelled anime in local-season chooser");
                }
            }
            app.Shutdown();
            File.WriteAllLines(args[1],Report); foreach(string line in Report) Console.WriteLine(line); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
