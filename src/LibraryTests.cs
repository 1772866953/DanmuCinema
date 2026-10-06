using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DanmuCinema
{
    public static class LibraryTests
    {
        static Dictionary<string, object> Video(int id, string path, long size = 0, string date = "2026-01-01T00:00:00Z")
        {
            return new Dictionary<string, object> { { "Id", id.ToString() }, { "Name", Path.GetFileNameWithoutExtension(path) }, { "Path", path }, { "SeriesName", "示例番剧S2" }, { "Type", "Episode" }, { "ParentIndexNumber", 2 },
                { "DateLastSaved", date }, { "MediaSources", new object[] { new Dictionary<string, object> { { "Size", size }, { "Bitrate", size * 10 } } } } };
        }
        sealed class LibraryHandler : HttpMessageHandler
        {
            public readonly List<int> Starts = new List<int>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                if (query["SearchTerm"] != null) throw new Exception("筛选不应作为远端搜索条件");
                int start = Int32.Parse(query["StartIndex"]); Starts.Add(start);
                var rows = Enumerable.Range(start, Math.Min(250, 501 - start)).Select(n => Video(n, Path.Combine(Paths.Root, "large", "Show S02E" + n.ToString("000") + ".mkv"))).ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json.Write(new { Items = rows, TotalRecordCount = 501 })) });
            }
        }
        sealed class SourceHandler : HttpMessageHandler
        {
            public readonly List<string> Hosts = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                Hosts.Add(request.RequestUri.Host); string data;
                if (request.RequestUri.AbsolutePath.EndsWith("search/anime")) data = "{\"success\":true,\"animes\":[{\"animeId\":1,\"animeTitle\":\"示例番剧 第二季\",\"typeDescription\":\"动漫\"}]}";
                else if (request.RequestUri.AbsolutePath.Contains("bangumi")) data = "{\"success\":true,\"bangumi\":{\"episodes\":[{\"episodeId\":11,\"episodeNumber\":\"1\",\"episodeTitle\":\"第一集\"},{\"episodeId\":12,\"episodeNumber\":\"2\",\"episodeTitle\":\"第二集\"}]}}";
                else data = "{\"comments\":[{\"cid\":1,\"p\":\"1,1,16777215,user\",\"m\":\"" + request.RequestUri.Host + "\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data) });
            }
        }
        sealed class TestGrid : MediaGrid
        {
            public TestGrid(LibrarySelection selection) : base(selection) { }
            public void Down(Point point) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
            public void DragTo(Point point) { OnMouseMove(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
            public void Up(Point point) { OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
            public void Key(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
        }
        public static async Task Run(List<string> report)
        {
            var settings = new AppSettings { EncryptedToken = SettingsStore.Protect("synthetic-library-token"), EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, EnableDandan = false, EncryptedAdditionalApis = SettingsStore.Protect("来源一|https://one.test\n来源二|https://two.test") };
            var paging = new LibraryHandler();
            using (var api = new JellyfinApi(settings, paging))
            {
                var items = await api.Items("");
                SelfTests.Assert(items.Length == 501 && paging.Starts.SequenceEqual(new[] { 0, 250, 500 }), "媒体库默认读取全部 501 个视频并遍历所有分页", report);
            }
            string folder = Path.Combine(Paths.Root, "library-fixture"); Directory.CreateDirectory(folder);
            var videos = new[] {
                Video(1, Path.Combine(folder, "A S02E01.mkv"), 10000000000L, "2026-01-03T00:00:00Z"),
                Video(2, Path.Combine(folder, "B S02E02.mkv"), 2000000000L, "2026-01-01T00:00:00Z"),
                Video(3, Path.Combine(folder, "C S02E03.mkv"), 500000000L, "2026-01-02T00:00:00Z") };
            var model = new MediaLibrary(); model.Replace(videos);
            SelfTests.Assert(model.View("", LibrarySort.Name, false).Select(x => Json.Text(x.Item, "Id")).SequenceEqual(new[] { "1", "2", "3" }) && model.View("S02E02", LibrarySort.Name, false).Length == 1, "空筛选显示全部，文件名筛选只影响显示", report);
            SelfTests.Assert(model.View("示例番剧 S02E02", LibrarySort.Name, false).Length == 1 && model.View("不存在", LibrarySort.Name, false).Length == 0, "筛选支持中文番剧名、文件路径及多词组合", report);
            SelfTests.Assert(model.View("", LibrarySort.Size, true).Select(x => Json.Text(x.Item, "Id")).SequenceEqual(new[] { "1", "2", "3" }) && model.View("", LibrarySort.Size, false)[0].Size == 500000000L, "文件大小使用数值升降序，10 GB 不会排在 2 GB 之后", report);
            SelfTests.Assert(model.View("", LibrarySort.Modified, true).Select(x => Json.Text(x.Item, "Id")).SequenceEqual(new[] { "1", "3", "2" }), "修改日期按真实时间排序", report);
            model.Selection.Set(model.Entries[0].Key, true); model.Selection.Set(model.Entries[1].Key, true);
            model.View("C", LibrarySort.Size, true);
            SelfTests.Assert(model.SelectedItems.Length == 2, "筛选和排序保留已勾选的隐藏影片", report);
            model.Replace(videos.Skip(1)); SelfTests.Assert(model.SelectedItems.Length == 1 && Json.Text(model.SelectedItems[0], "Id") == "2", "刷新仅移除已不存在的选择，其他勾选仍保留", report);
            var keys = new[] { "a", "b", "c", "d" };
            SelfTests.Assert(LibrarySelection.DragRange(keys, 3, 1, new string[0], false).OrderBy(x => x).SequenceEqual(new[] { "b", "c", "d" }) && LibrarySelection.DragRange(keys, 2, 3, new[] { "a" }, true).OrderBy(x => x).SequenceEqual(new[] { "a", "c", "d" }), "反向圈选和 Ctrl 追加圈选选中完整区间", report);
            model.Replace(videos); model.Selection.Clear();
            OnSta(() => GridChecks(model, report));
            foreach (var video in videos) File.WriteAllText(Json.Text(video, "Path"), "synthetic-video");
            DateTime actualTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc); File.SetLastWriteTimeUtc(Json.Text(videos[0], "Path"), actualTime);
            model.Replace(videos);
            SelfTests.Assert(model.Entries[0].Size == new FileInfo(Json.Text(videos[0], "Path")).Length && model.Entries[0].ModifiedUtc == actualTime, "本地文件真实大小和修改时间优先于服务器旧元数据", report);
            MediaLibrary.RequireSameSeason(videos.Take(2));
            var otherSeason = Video(4, Path.Combine(folder, "Other S01E01.mkv")); otherSeason["ParentIndexNumber"] = 1;
            bool rejected = false; try { MediaLibrary.RequireSameSeason(new[] { videos[0], otherSeason }); } catch (InvalidOperationException) { rejected = true; }
            SelfTests.Assert(rejected, "勾选批量匹配拒绝混入其他季度", report);
            var handler = new SourceHandler();
            using (var api = new JellyfinApi(settings)) using (var catalog = new DanmuCatalog(settings, api, handler))
            {
                var choices = catalog.SourceChoices();
                var result = await catalog.Search("示例番剧", true, false, 2, Json.Text(choices[1], "Id"));
                SelfTests.Assert(result.Items.Length == 1 && handler.Hosts.All(x => x == "two.test") && result.Sources.Length == 1, "指定接口搜索只请求该服务并隔离其他来源", report);
                rejected = false; try { await catalog.Search("示例番剧", true, false, 2, "disabled-provider"); } catch (InvalidOperationException) { rejected = true; }
                SelfTests.Assert(rejected, "已停用或不存在的接口不会悄悄回退到其他服务", report);
                var episodes = (await catalog.Episodes((Dictionary<string, object>)result.Items[0])).Cast<Dictionary<string, object>>().ToArray();
                var plan = BatchMatching.Plan(videos.Take(2), episodes);
                string oldXml = "<i><d p=\"1,1,25,16777215\">旧来源</d></i>";
                string firstXml = Path.ChangeExtension(Json.Text(videos[0], "Path"), ".xml"); File.WriteAllText(firstXml, oldXml);
                var downloaded = await BatchDownloads.Run(plan, episode => catalog.Download(episode), false, CancellationToken.None, null, 0, entry => catalog.RecordAssociation(entry.Local, entry.Remote));
                model.Replace(videos);
                SelfTests.Assert(downloaded.Saved == 2 && File.ReadAllText(firstXml).Contains("two.test") && File.ReadAllText(firstXml + ".bak") == oldXml && model.Entries.Take(2).All(x => x.SourceLabel == "来源二") && !model.Entries[2].HasXml, "重新选择仅替换勾选文件，备份旧 XML 并更新弹幕列来源", report);
                int calls = 0;
                downloaded = await BatchDownloads.Run(plan, episode => { calls++; return catalog.Download(episode); }, true, CancellationToken.None, null, 0);
                SelfTests.Assert(downloaded.Skipped == 2 && calls == 0, "保留已有 XML 选项仍有效", report);
                File.SetLastWriteTimeUtc(firstXml, actualTime);
                model.Replace(videos);
                SelfTests.Assert(model.Entries[0].HasXml && model.Entries[0].SourceLabel == "" && model.Entries[1].SourceLabel == "来源二", "外部更改 XML 后不显示过期来源归属，其他记录仍可恢复", report);
                OnSta(() =>
                {
                    using (var dialog = new MatchDialog(catalog, videos[0], "", true, videos.Take(2), DanmuMatchScope.Selection))
                    {
                        var tabs = Descendants(dialog).OfType<TabControl>().Single();
                        var service = Descendants(dialog).OfType<ComboBox>().Single();
                        SelfTests.Assert(tabs.TabPages.Count == 3 && (DanmuMatchScope)tabs.SelectedTab.Tag == DanmuMatchScope.Selection && service.Items.Count == 3, "匹配窗口提供单集、整季、已选影片选项卡和接口选择", report);
                    }
                    using (var dialog = new MatchDialog(catalog, null, "", true))
                        SelfTests.Assert(Descendants(dialog).OfType<TabControl>().Single().TabPages.Count == 1, "未选择影片时在线搜索不提供无目标的整季下载", report);
                });
            }
        }
        static IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw new Exception("控件自检失败", failure);
        }
        static void GridChecks(MediaLibrary model, List<string> report)
        {
            using (var grid = new TestGrid(model.Selection) { Size = new Size(1100, 400) })
            {
                // An unparented control is hosted by WinForms' hidden parking window.
                // Its rows have usable coordinates without showing a form or injecting desktop input.
                IntPtr gridHandle = grid.Handle;
                grid.PerformLayout(); grid.SetEntries(model.Entries);
                SelfTests.Assert(grid.Rows.Count == 3 && model.Selection.Count == 0 && grid.Columns.Contains("modified") && grid.Columns.Contains("danmu"), "列表绑定默认显示所有行，包含复选框、日期和弹幕列", report);
                Rectangle first = grid.GetCellDisplayRectangle(0, 0, true), second = grid.GetCellDisplayRectangle(0, 1, true);
                var p1 = new Point(first.Left + 12, first.Top + first.Height / 2); var p2 = new Point(second.Left + 12, second.Top + second.Height / 2);
                grid.Down(p1); grid.Up(p1); grid.Down(p2); grid.Up(p2);
                SelfTests.Assert(model.Selection.Count == 2 && grid.Rows[0].Selected && grid.Rows[1].Selected && Convert.ToBoolean(grid.Rows[0].Cells[0].Value), "真实控件鼠标复选可逐行追加，勾选与高亮同步", report);
                grid.Key(Keys.Space); grid.Key(Keys.Space);
                SelfTests.Assert(model.Selection.Count == 2 && grid.CurrentRow.Index == 1, "复选框定位当前行，空格切换该行且保留其他勾选", report);
                grid.SetEntries(model.View("C", LibrarySort.Name, false)); grid.SelectVisible(true); grid.SetEntries(model.View("", LibrarySort.Name, false));
                SelfTests.Assert(model.Selection.Count == 3 && grid.Rows.Cast<DataGridViewRow>().All(x => Convert.ToBoolean(x.Cells[0].Value)), "全选当前筛选不会清除隐藏的选择，恢复筛选后勾选可见", report);
                grid.ClearChecked(); grid.Key(Keys.Control | Keys.A);
                SelfTests.Assert(model.Selection.Count == 3, "列表 Ctrl+A 全选当前列表", report);
                grid.ClearChecked();
                var start = grid.GetCellDisplayRectangle(1, 0, true); var end = grid.GetCellDisplayRectangle(1, 2, true);
                p1 = new Point(start.Left + 25, start.Top + start.Height / 2); p2 = new Point(end.Left + 70, end.Top + end.Height / 2);
                grid.Down(p1); grid.DragTo(p2); grid.Up(p2);
                SelfTests.Assert(model.Selection.Count == 3 && grid.Rows.Cast<DataGridViewRow>().All(x => x.Selected), "真实控件鼠标拖动圈选跨行选中并同步复选框", report);
                grid.ClearChecked(); SelfTests.Assert(model.Selection.Count == 0, "取消全部选择包括隐藏勾选项", report);
                var many = MediaLibrary.Build(Enumerable.Range(10, 40).Select(n => Video(n, Path.Combine(Paths.Root, "scroll", "Episode " + n + ".mkv"))));
                grid.SetEntries(many); grid.FirstDisplayedScrollingRowIndex = 8;
                grid.SetEntries(many);
                SelfTests.Assert(grid.FirstDisplayedScrollingRowIndex == 8, "自动刷新列表保留滚动位置，不跳回第一行", report);
            }
        }
    }
}
