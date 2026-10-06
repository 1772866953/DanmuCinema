using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class CatalogTests
    {
        sealed class FixtureHandler : HttpMessageHandler
        {
            public int Active, MaxActive;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                int active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
                try
                {
                    await Task.Delay(35, cancellation);
                    if (request.RequestUri.Host == "failed.test") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
                    string data;
                    if (request.RequestUri.AbsolutePath.EndsWith("search/anime"))
                        data = "{\"success\":true,\"animes\":[{\"animeId\":1,\"animeTitle\":\"骸骨骑士 第二季\",\"typeDescription\":\"动漫\",\"startDate\":\"2026-07-06\",\"episodeCount\":12},{\"animeId\":2,\"animeTitle\":\"无关电影\",\"typeDescription\":\"电影\"}]}";
                    else if (request.RequestUri.AbsolutePath.Contains("/bangumi/"))
                        data = "{\"success\":true,\"bangumi\":{\"episodes\":[{\"episodeId\":11,\"episodeNumber\":\"3\",\"episodeTitle\":\"第三集\"}]}}";
                    else data = "{\"comments\":[{\"cid\":2,\"p\":\"10,1,16777215,user\",\"m\":\"后出现\"},{\"cid\":1,\"p\":\"1.25,5,16777215,user\",\"m\":\"中文<&弹幕\"}]}";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
                }
                finally { Interlocked.Decrement(ref Active); }
            }
        }
        public static async Task Run(List<string> report)
        {
            SelfTests.Assert(new AppSettings().EnableAnimeko && new AppSettings().EnableBahamut && Json.Read<AppSettings>("{\"Port\":8096}").EnableAnimeko, "已有配置升级后默认启用动漫源", report);
            SelfTests.Assert(DanmuCatalog.Chinese("骸骨骑士", true) == "骸骨騎士" && DanmuCatalog.Chinese("動畫", false) == "动画", "巴哈搜索繁简体转换", report);
            SelfTests.Assert(DanmuCatalog.ValidateApiRoot("https://server.test/key/api/v2/") == "https://server.test/key", "自定义 API 根地址规范化", report);
            bool rejected = false; try { DanmuCatalog.ValidateApiRoot("file:///C:/Videos"); } catch (ArgumentException) { rejected = true; }
            SelfTests.Assert(rejected, "自定义来源拒绝非 HTTP 协议", report);
            var settings = new AppSettings { EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false, EnableDandan = false, EncryptedAdditionalApis = SettingsStore.Protect("动漫源一|https://one.test\n动漫源二|https://two.test\n故障源|https://failed.test"), EncryptedToken = SettingsStore.Protect("synthetic-token"), DanmuPort = FreePort() };
            var handler = new FixtureHandler();
            using (var api = new JellyfinApi(settings))
            {
                var catalog = new DanmuCatalog(settings, api, handler);
                var result = await catalog.Search("骸骨骑士", true);
                SelfTests.Assert(handler.MaxActive >= 2 && result.Sources.Length == 3 && result.Items.Length == 2 && result.HiddenCount == 2, "多来源并发查询、动漫过滤及单源故障隔离", report);
                var choices = result.Items.Cast<Dictionary<string, object>>().ToArray();
                SelfTests.Assert(Json.Text(choices[0], "Id") != Json.Text(choices[1], "Id"), "不同来源相同远端 ID 不混淆", report);
                var eps = await catalog.Episodes(choices[0]);
                string xml = await catalog.Download((Dictionary<string, object>)eps[0]);
                SelfTests.Assert(DanmuCatalog.ParseXml(xml).GetElementsByTagName("d")[0].InnerText == "中文<&弹幕" && xml.Contains("1.25,5,25,16777215"), "自定义源选集、中文弹幕下载及 XML 转义", report);
                catalog.Dispose();
                catalog = new DanmuCatalog(settings, api, new FixtureHandler());
                var detail = await catalog.Route("/api/v2/bangumi/" + Json.Text(choices[0], "Id"));
                SelfTests.Assert(detail.Content.Contains("第三集"), "重启后番剧 ID 可通过持久缓存恢复", report);
                var reply = await catalog.Route("/api/v2/comment/" + Json.Text((Dictionary<string, object>)eps[0], "Id") + "?format=json");
                var restoredComments = Json.Array(Json.Object(reply.Content), "Comments");
                SelfTests.Assert(restoredComments.Length == 2 && Json.Text((Dictionary<string, object>)restoredComments[0], "M") == "中文<&弹幕" && Json.Text((Dictionary<string, object>)restoredComments[1], "P").StartsWith("10,"), "重启后弹幕 ID 恢复并按时间输出播放器 JSON", report);
                using (var gateway = new DanmuGateway(settings, catalog))
                {
                    gateway.Start();
                    using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
                    {
                        string root = "http://127.0.0.1:" + settings.DanmuPort + "/" + gateway.Key;
                        string search = await client.GetStringAsync(root + "/api/v2/search/anime?keyword=" + Uri.EscapeDataString("骸骨骑士"));
                        var data = Json.Object(search);
                        SelfTests.Assert(Json.Array(data, "Animes").Length == 2 && Json.Array(data, "SourceStatus").Length == 3, "iPad 网关实际返回多来源搜索与故障状态", report);
                        string content = await client.GetStringAsync(root + "/api/v2/comment/" + Json.Text((Dictionary<string, object>)eps[0], "Id") + "?format=xml");
                        SelfTests.Assert(DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count == 2 && DanmuCatalog.ParseXml(content).GetElementsByTagName("d")[0].InnerText == "中文<&弹幕", "iPad 网关实际输出按时间排序的新增来源 XML", report);
                    }
                    await gateway.Stop();
                }
            }
            var animekoRows = Json.Array(Json.Object("{\"rows\":[{\"id\":\"test\",\"danmakuInfo\":{\"playTime\":1250,\"location\":\"BOTTOM\",\"color\":-1,\"text\":\"测试\"}}]}"), "Rows");
            var bahamutRows = Json.Array(Json.Object("{\"rows\":[{\"sn\":1,\"time\":25,\"position\":1,\"color\":\"#FF0000\",\"text\":\"测试\"}]}"), "Rows");
            SelfTests.Assert(DanmuCatalog.CommentsToXml(animekoRows, "animeko").Contains("1.25,4,25,16777215") && DanmuCatalog.CommentsToXml(bahamutRows, "bahamut").Contains("2.5,5,25,16711680"), "两个动漫源的时间单位、位置和颜色转换", report);
            rejected = false; try { DanmuCatalog.ParseXml("<!DOCTYPE i [<!ENTITY x SYSTEM 'file:///C:/secret'>]><i>&x;</i>"); } catch (System.Xml.XmlException) { rejected = true; }
            SelfTests.Assert(rejected, "弹幕 XML 拒绝外部实体", report);
            string unordered = "<i><chatserver>fixture</chatserver><d p=\"10,1,25,16777215,0,0,u,3\">晚</d><d p=\"2.5,5,25,255,0,0,u,1\" custom=\"keep\">早&lt;&amp;</d><d p=\"2.5,4,25,255,0,0,u,2\">同秒</d></i>";
            string sorted = DanmuCatalog.SortXmlForPlayback(unordered);
            var sortedXml = DanmuCatalog.ParseXml(sorted); var sortedRows = sortedXml.GetElementsByTagName("d");
            SelfTests.Assert(sortedRows[0].InnerText == "早<&" && sortedRows[1].InnerText == "同秒" && sortedRows[2].InnerText == "晚" && ((System.Xml.XmlElement)sortedRows[0]).GetAttribute("custom") == "keep" && sortedXml.GetElementsByTagName("chatserver")[0].InnerText == "fixture", "跳转兼容排序保留同秒顺序、时间、正文、属性与元数据", report);
            SelfTests.Assert(DanmuCatalog.SortXmlForPlayback(sorted) == sorted, "已排序弹幕保持原始文件内容且不重复改写", report);
            rejected = false; try { DanmuCatalog.SortXmlForPlayback("<i><d p=\"NaN,1,25,1\">无效</d></i>"); } catch (InvalidDataException) { rejected = true; }
            SelfTests.Assert(rejected, "无效弹幕时间拒绝保存，已有文件不受影响", report);
        }
        static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    }
}
