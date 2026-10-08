using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DanmuCinema
{
    public sealed class CatalogSearch
    {
        public object[] Items;
        public object[] Sources;
        public int HiddenCount;
        public string Summary;
    }
    public sealed class CatalogReply
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public string Content;
    }
    public sealed partial class DanmuCatalog : IDisposable
    {
        readonly AppSettings settings;
        readonly JellyfinApi api;
        readonly HttpClient http;
        public readonly DandanApiCache Cache;
        readonly object sync = new object();
        readonly Dictionary<string, Dictionary<string, object>> animes = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<string, Dictionary<string, object>> episodes = new Dictionary<string, Dictionary<string, object>>();
        string CachePath { get { return Path.Combine(Paths.Data, "anime-catalog-cache.json"); } }
        sealed class Provider { public string Id, Name, Url; }
        public DanmuCatalog(AppSettings settings, JellyfinApi api, HttpMessageHandler handler = null)
        {
            this.settings = settings; this.api = api;
            Cache = new DandanApiCache(settings);
            // External anime services follow Windows' configured proxy. LAN Jellyfin
            // requests still use the dedicated proxy-free JellyfinApi client.
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DanmuCinema/1.2");
            try
            {
                if (File.Exists(CachePath))
                {
                    var saved = Json.Object(File.ReadAllText(CachePath));
                    foreach (Dictionary<string, object> a in Json.Array(saved, "Animes").Take(1000)) animes[Json.Text(a, "Id")] = a;
                    foreach (Dictionary<string, object> e in Json.Array(saved, "Episodes").Take(5000)) episodes[Json.Text(e, "Id")] = e;
                }
            }
            catch { Log.Write("动漫搜索缓存无法读取，将重新搜索。媒体库和弹幕文件未更改。"); }
        }
        public static string StableNumber(string identity)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                // Exactly representable in JavaScript for numeric API IDs.
                long value = (BitConverter.ToInt64(bytes, 0) & 0x000FFFFFFFFFFFFFL) + 1;
                return value.ToString(CultureInfo.InvariantCulture);
            }
        }
        public static string ValidateApiRoot(string root)
        {
            Uri uri;
            if (!Uri.TryCreate(root.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("自定义 API 需填写完整 http(s) 地址；密钥可放在路径中，不支持查询参数或用户名密码。");
            string value = uri.AbsoluteUri.TrimEnd('/');
            if (value.EndsWith("/api/v2", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 7);
            return value;
        }
        public static void ValidateAdditionalApis(string text)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 5) throw new ArgumentException("最多添加 5 个自定义 API。");
            foreach (string line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length != 2 || String.IsNullOrWhiteSpace(parts[0]) || parts[0].Trim().Length > 40) throw new ArgumentException("每行格式：来源名称|API 根地址。");
                ValidateApiRoot(parts[1]);
            }
        }
        List<Provider> Providers()
        {
            var list = new List<Provider>();
            if (settings.EnableDandan) list.Add(new Provider { Id = "dandan", Name = "弹弹play 官方", Url = "https://api.dandanplay.net" });
            if (settings.EnableExistingDanmu) list.Add(new Provider { Id = "jellyfin", Name = "现有平台", Url = "" });
            if (settings.EnableAnimeko) list.Add(new Provider { Id = "animeko", Name = "Animeko", Url = "https://api.animeko.org" });
            if (settings.EnableBahamut) list.Add(new Provider { Id = "bahamut", Name = "巴哈姆特动画疯", Url = "https://api.gamer.com.tw" });
            string custom = SettingsStore.Unprotect(settings.EncryptedAdditionalApis);
            ValidateAdditionalApis(custom);
            foreach (string line in custom.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|'); string root = ValidateApiRoot(parts[1]);
                list.Add(new Provider { Id = "custom-" + StableNumber(root), Name = parts[0].Trim(), Url = root });
            }
            return list;
        }
        Provider GetProvider(Dictionary<string, object> item)
        {
            string id = Json.Text(item, "Provider");
            var p = Providers().FirstOrDefault(x => x.Id == id);
            if (p == null) throw new InvalidOperationException("这个弹幕来源已停用或地址已更改，请重新搜索。");
            return p;
        }
        async Task<string> Fetch(Provider provider, string path, object body = null, CancellationToken cancellation = default(CancellationToken), string anime = null)
        {
            if (provider.Id != "dandan") return await FetchNetwork(provider, path, body, cancellation).ConfigureAwait(false);
            var credentials = DandanConfig.Load(); if (!credentials.Ready) throw new InvalidOperationException("官方源未就绪");
            string kind = path.StartsWith("/api/v2/comment/") ? "comment" : path == "/api/v2/match" ? "match" : "search";
            string bodyKey = body == null ? "" : Json.Write(body);
            if (path == "/api/v2/match" && body != null)
            {
                var match = Json.Object(bodyKey);
                // A hash-only lookup is independent of filename and Jellyfin's optional duration.
                if (Json.Text(match, "matchMode") == "hashOnly") bodyKey = Json.Write(new { fileHash = Json.Text(match, "fileHash"), fileSize = Json.Text(match, "fileSize"), matchMode = "hashOnly" });
            }
            string key = DandanApiCache.Key(credentials.AppId.Trim() + "|" + path + "|" + bodyKey);
            string label = path;
            if (body != null) label = Json.Text(Json.Object(Json.Write(body)), "fileName") + " · " + Json.Text(Json.Object(Json.Write(body)), "matchMode");
            else if (path.Contains("keyword=")) label = System.Web.HttpUtility.ParseQueryString(new Uri("https://api.dandanplay.net" + path).Query)["keyword"];
            return await Cache.Get(key, kind, label, async () =>
            { string content = await FetchNetwork(provider, path, body, cancellation).ConfigureAwait(false); EnsureSuccess(Json.Object(content)); return content; }, cancellation, anime).ConfigureAwait(false);
        }
        async Task<string> FetchNetwork(Provider provider, string path, object body, CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, provider.Url.TrimEnd('/') + path))
                {
                    if (body != null) request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                    if (provider.Id == "dandan")
                    {
                        var credentials = DandanConfig.Load();
                        string secret = credentials.Secret;
                        if (!credentials.Ready) throw new InvalidOperationException("官方源未就绪");
                        string timestamp = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString(CultureInfo.InvariantCulture);
                        request.Headers.TryAddWithoutValidation("X-AppId", credentials.AppId.Trim());
                        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);
                        using (var sha = SHA256.Create()) request.Headers.TryAddWithoutValidation("X-Signature", Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(credentials.AppId.Trim() + timestamp + path.Split('?')[0] + secret))));
                    }
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    {
                        if (provider.Id == "dandan" && path.StartsWith("/api/v2/comment/") && (int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                            return await RedirectedComments(request.RequestUri, response.Headers.Location, timeout.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode);
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var output = new MemoryStream())
                        {
                            var bytes = new byte[32768]; int count;
                            while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, timeout.Token)) > 0)
                            {
                                if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("响应过大");
                                output.Write(bytes, 0, count);
                            }
                            return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
                        }
                    }
                }
            }
        }
        public static bool AllowedCommentRedirect(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
            return new[] { "dandanplay.net", "dandanplay.com", "acplay.net" }.Any(domain => uri.Host == domain || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        }
        async Task<string> RedirectedComments(Uri origin, Uri location, CancellationToken cancellation)
        {
            for (int hop = 0; hop < 3; hop++)
            {
                var target = location == null ? null : new Uri(origin, location);
                if (!AllowedCommentRedirect(target)) throw new InvalidDataException("官方弹幕下载跳转地址无效。");
                // The acceleration service never receives app authentication headers.
                using (var request = new HttpRequestMessage(HttpMethod.Get, target))
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) { origin = target; location = response.Headers.Location; continue; }
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode);
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var bytes = new byte[32768]; int count;
                        while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false)) > 0) { if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("响应过大"); output.Write(bytes, 0, count); }
                        return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
                    }
                }
            }
            throw new InvalidDataException("弹幕下载跳转次数过多。");
        }
        static void EnsureSuccess(Dictionary<string, object> response)
        {
            if (Json.Text(response, "Success") == "False" || Json.Child(response, "Error") != null)
                throw new InvalidDataException("来源返回错误 " + Json.Text(response, "ErrorCode"));
        }
        static string First(Dictionary<string, object> item, params string[] fields)
        {
            foreach (string field in fields) { string value = Json.Text(item, field); if (!String.IsNullOrWhiteSpace(value)) return value; }
            return "";
        }
        Dictionary<string, object> Anime(Provider provider, string remote, string title, string date, string count, string category, string site = "")
        {
            if (String.IsNullOrWhiteSpace(remote) || String.IsNullOrWhiteSpace(title)) throw new InvalidDataException("作品缺少 ID 或名称");
            return new Dictionary<string, object> {
                { "Id", StableNumber(provider.Id + ":" + site + ":anime:" + remote) }, { "RemoteId", remote }, { "Provider", provider.Id }, { "SiteId", site },
                { "Name", title }, { "Year", date.Length >= 4 ? date.Substring(0, 4) : date }, { "StartDate", date }, { "EpisodeSize", count },
                { "Category", category }, { "Site", site == "" ? provider.Name : provider.Name + " · " + site }
            };
        }
        async Task<object[]> SearchProvider(Provider p, string keyword, CancellationToken cancellation)
        {
            using (var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
            searchTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var list = new List<object>();
            if (p.Id == "jellyfin")
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(searchTimeout.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var result = Json.Read<object[]>(await api.Request("GET", "api/danmu/search?keyword=" + Uri.EscapeDataString(keyword), null, true, timeout.Token));
                    foreach (Dictionary<string, object> a in result) list.Add(Anime(p, Json.Text(a, "Id"), Json.Text(a, "Name"), Json.Text(a, "Year"), Json.Text(a, "EpisodeSize"), Json.Text(a, "Category"), Json.Text(a, "SiteId")));
                }
            }
            else if (p.Id == "animeko")
            {
                var bgm = new Provider { Id = "bangumi", Url = "https://api.bgm.tv" };
                var result = Json.Object(await Fetch(bgm, "/v0/search/subjects?limit=25", new { keyword = keyword, filter = new { type = new[] { 2 } } }, searchTimeout.Token));
                foreach (Dictionary<string, object> a in Json.Array(result, "Data"))
                    list.Add(Anime(p, Json.Text(a, "Id"), First(a, "NameCn", "Name"), Json.Text(a, "Date"), Json.Text(a, "Eps"), "动漫"));
            }
            else if (p.Id == "bahamut")
            {
                var result = Json.Object(await Fetch(p, "/mobile_app/anime/v1/search.php?kw=" + Uri.EscapeDataString(Chinese(keyword, true)), null, searchTimeout.Token));
                EnsureSuccess(result);
                // Local folder titles often omit words from the licensed title.
                // If the exact query has no results, retry a short Chinese title
                // while preserving every candidate's complete season/year label.
                bool shortened = false;
                var prefix = Regex.Match(keyword, @"^[\u4e00-\u9fff]{6,}");
                if (Json.Array(result, "Anime").Length == 0 && prefix.Success)
                {
                    result = Json.Object(await Fetch(p, "/mobile_app/anime/v1/search.php?kw=" + Uri.EscapeDataString(Chinese(prefix.Value.Substring(0, 4), true)), null, searchTimeout.Token));
                    EnsureSuccess(result); shortened = true;
                }
                foreach (Dictionary<string, object> a in Json.Array(result, "Anime"))
                {
                    string info = Json.Text(a, "Info");
                    var choice = Anime(p, Json.Text(a, "VideoSn"), Chinese(Json.Text(a, "Title"), false), Regex.Match(info, @"\d{4}").Value, Regex.Match(info, @"共\s*(\d+)").Groups[1].Value, "动漫");
                    if (shortened) choice["SearchNote"] = "短标题回退";
                    list.Add(choice);
                }
            }
            else
            {
                var result = Json.Object(await Fetch(p, "/api/v2/search/anime?keyword=" + Uri.EscapeDataString(keyword), null, searchTimeout.Token));
                EnsureSuccess(result);
                foreach (Dictionary<string, object> a in Json.Array(result, "Animes"))
                    list.Add(Anime(p, Json.Text(a, "AnimeId"), Json.Text(a, "AnimeTitle"), Json.Text(a, "StartDate"), Json.Text(a, "EpisodeCount"), First(a, "TypeDescription", "Type")));
            }
            return list.ToArray();
            }
        }
        public Dictionary<string, object>[] SourceChoices()
        {
            return Providers().Select(x => new Dictionary<string, object> { { "Id", x.Id }, { "Name", x.Name } }).ToArray();
        }
        public void RecordAssociation(Dictionary<string, object> video, Dictionary<string, object> episode)
        {
            try
            {
                var provider = GetProvider(episode);
                Dictionary<string, object> anime;
                lock (sync) animes.TryGetValue(Json.Text(episode, "AnimeId"), out anime);
                DanmuAssociations.Save(Json.Text(video, "Path"), new DanmuAssociation {
                    Source = anime == null ? provider.Name : Json.Text(anime, "Site"), Provider = provider.Id,
                    Anime = Json.Text(anime, "Name"), Episode = Json.Text(episode, "Number") + " " + Json.Text(episode, "Title"), CommentId = Json.Text(episode, "CommentId")
                });
            }
            catch { Log.Write("弹幕 XML 已保存，但来源记录未更新；可重新选择来源。"); }
        }
        public string EpisodeSource(Dictionary<string, object> episode)
        {
            string site = Json.Text(episode, "Site"); if (site != "") return site;
            Dictionary<string, object> anime; lock (sync) animes.TryGetValue(Json.Text(episode, "AnimeId"), out anime);
            site = Json.Text(anime, "Site"); if (site != "") return site;
            try { return GetProvider(episode).Name; } catch { return Json.Text(episode, "Provider"); }
        }
        public async Task<CatalogSearch> Search(string keyword, bool animeOnly, bool smart = true, int season = 0, string providerId = null, CancellationToken cancellation = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(keyword)) throw new ArgumentException("请输入作品名。");
            if (keyword.Length > 200) throw new ArgumentException("搜索词过长。");
            if (season == 0 && smart) season = SmartMatching.SeasonTitle(keyword);
            var providers = Providers();
            if (!String.IsNullOrEmpty(providerId))
            {
                providers = providers.Where(x => x.Id == providerId).ToList();
                if (providers.Count == 0) throw new InvalidOperationException("所选接口已停用，请重新选择弹幕接口。");
            }
            var tasks = providers.Select(async p =>
            {
                try
                {
                    var found = new List<Dictionary<string, object>>(); string note = "";
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(15));
                        var queries = smart ? SmartMatching.Queries(keyword) : new[] { keyword.Trim() };
                        for (int attempt = 0; attempt < queries.Length; attempt++)
                        {
                            Exception failure = null; object[] rows = null;
                            try { rows = await SearchProvider(p, queries[attempt], deadline.Token); } catch (Exception error) { failure = error; }
                            if (failure != null) { if (found.Count == 0) throw failure; break; }
                            found.AddRange(rows.Cast<Dictionary<string, object>>());
                            if (attempt > 0) note = "智能搜索回退";
                            if (rows.Cast<Dictionary<string, object>>().Any(x => Json.Text(x, "SearchNote") != "")) note = "短标题回退";
                            if (!smart || found.Any(x => SmartMatching.Score(keyword, x, season) >= 70 && (season == 0 || SmartMatching.SeasonTitle(Json.Text(x, "Name")) == season))) break;
                        }
                    }
                    var result = found.GroupBy(x => Json.Text(x, "Id")).Select(x => (object)x.First()).ToArray();
                    return new { Items = result, State = new Dictionary<string, object> { { "Name", p.Name }, { "Count", result.Length }, { "Error", "" }, { "Note", note } } };
                }
                catch (Exception e)
                {
                    cancellation.ThrowIfCancellationRequested();
                    // Do not log endpoint URLs, path tokens, AppSecret, or arbitrary upstream messages.
                    string error = e is OperationCanceledException ? "请求超时" : p.Id == "dandan" && e is InvalidOperationException && e.Message.StartsWith("官方源") ? "未就绪" : e is InvalidOperationException && e.Message.StartsWith("HTTP ") ? e.Message : "来源请求失败";
                    Log.Write("弹幕来源「" + p.Name + "」：" + error);
                    return new { Items = new object[0], State = new Dictionary<string, object> { { "Name", p.Name }, { "Count", 0 }, { "Error", error } } };
                }
            }).ToArray();
            var parts = await Task.WhenAll(tasks);
            var all = parts.SelectMany(x => x.Items).Cast<Dictionary<string, object>>().GroupBy(x => Json.Text(x, "Id")).Select(x => x.First()).ToArray();
            var trustedTitles = all.Where(IsAnime).Select(a => Json.Text(a, "Name")).ToArray();
            var filtered = all.Where(a => !animeOnly || IsAnime(a) || trustedTitles.Any(t => SimilarTitle(t, Json.Text(a, "Name"))))
                .OrderByDescending(a => Json.Text(a, "Provider") == "dandan").ThenByDescending(a => SmartMatching.Score(keyword, a, season)).ThenByDescending(a => IsAnime(a)).ThenByDescending(a => Json.Text(a, "Provider") == "bahamut").ThenByDescending(a => Json.Text(a, "Year")).ToArray();
            lock (sync)
            {
                foreach (var a in filtered) animes[Json.Text(a, "Id")] = a;
                SaveCache();
            }
            var states = parts.Select(x => (object)x.State).ToArray();
            string summary = String.Join("；", parts.Select(x => Json.Text(x.State, "Name") + "：" + (Json.Text(x.State, "Error") == "" ? Json.Text(x.State, "Count") + " 个" + (Json.Text(x.State, "Note") == "" ? "" : "（" + Json.Text(x.State, "Note") + "）") : Json.Text(x.State, "Error"))));
            return new CatalogSearch { Items = filtered.Cast<object>().ToArray(), Sources = states, HiddenCount = all.Length - filtered.Length, Summary = summary };
        }
        public static bool IsAnime(Dictionary<string, object> item)
        {
            string p = Json.Text(item, "Provider"), category = Json.Text(item, "Category");
            return p == "animeko" || p == "bahamut" || Regex.IsMatch(category, "动漫|动画|動畫|番剧|番劇|anime|ova|ona|剧场|劇場", RegexOptions.IgnoreCase);
        }
        public async Task<Dictionary<string, object>[]> LocalSeason(Dictionary<string, object> item)
        {
            var items = await api.Items("");
            return items.Cast<Dictionary<string, object>>().Where(x => SmartMatching.SameSeason(item, x)).ToArray();
        }
        public static bool SimilarTitle(string first, string second)
        {
            string a = Regex.Replace(Chinese(first, false).ToLowerInvariant(), @"[\s\p{P}\p{S}]", ""), b = Regex.Replace(Chinese(second, false).ToLowerInvariant(), @"[\s\p{P}\p{S}]", "");
            if (a.Length < 4 || b.Length < 4) return a == b && a.Length > 0;
            return a.Contains(b) || b.Contains(a) || a.Substring(0, 4) == b.Substring(0, 4);
        }
        public async Task<object[]> Episodes(Dictionary<string, object> anime, CancellationToken cancellation = default(CancellationToken))
        {
            var p = GetProvider(anime); string remote = Uri.EscapeDataString(Json.Text(anime, "RemoteId"));
            var list = new List<object>();
            if (p.Id == "jellyfin")
            {
                string site = Json.Text(anime, "SiteId");
                if (!Regex.IsMatch(site, "^[a-z0-9_-]+$")) throw new InvalidDataException("来源标识无效");
                var rows = Json.Read<object[]>(await api.Request("GET", "api/" + site + "/danmu/" + remote + "/episodes", null, true, cancellation));
                foreach (Dictionary<string, object> row in rows) list.Add(Episode(anime, MediaNames.CommentId(row), Json.Text(row, "Number"), Json.Text(row, "Title")));
            }
            else if (p.Id == "animeko")
            {
                var result = Json.Object(await Fetch(p, "/v2/subjects/" + remote, null, cancellation));
                foreach (Dictionary<string, object> e in Json.Array(result, "Episodes"))
                    if (Json.Text(e, "Type") == "MAIN") list.Add(Episode(anime, Json.Text(e, "EpisodeId"), First(e, "Sort", "Ep"), First(e, "NameCn", "Name")));
            }
            else if (p.Id == "bahamut")
            {
                var result = Json.Object(await Fetch(p, "/anime/v1/video.php?videoSn=" + remote, null, cancellation)); EnsureSuccess(result);
                var detail = Json.Child(Json.Child(result, "Data"), "Anime");
                var groups = Json.Child(detail, "Episodes");
                if (groups != null)
                    foreach (var group in groups)
                        foreach (Dictionary<string, object> e in Json.Array(groups, group.Key)) list.Add(Episode(anime, Json.Text(e, "VideoSn"), Json.Text(e, "Episode"), group.Key == "0" ? "" : "特别篇"));
            }
            else
            {
                var result = Json.Object(await Fetch(p, "/api/v2/bangumi/" + remote, null, cancellation, Json.Text(anime, "Name"))); EnsureSuccess(result);
                var bangumi = Json.Child(result, "Bangumi");
                foreach (Dictionary<string, object> e in Json.Array(bangumi, "Episodes")) list.Add(Episode(anime, Json.Text(e, "EpisodeId"), Json.Text(e, "EpisodeNumber"), Json.Text(e, "EpisodeTitle")));
            }
            lock (sync) { foreach (Dictionary<string, object> e in list) episodes[Json.Text(e, "Id")] = e; SaveCache(); }
            return list.ToArray();
        }
        static Dictionary<string, object> Episode(Dictionary<string, object> a, string remote, string number, string title)
        {
            if (String.IsNullOrWhiteSpace(remote)) throw new InvalidDataException("来源缺少弹幕 ID");
            return new Dictionary<string, object> { { "Id", StableNumber(Json.Text(a, "Provider") + ":" + Json.Text(a, "SiteId") + ":episode:" + remote) },
                { "AnimeId", Json.Text(a, "Id") }, { "AnimeTitle", Json.Text(a, "Name") }, { "Provider", Json.Text(a, "Provider") }, { "Site", Json.Text(a, "Site") }, { "SiteId", Json.Text(a, "SiteId") }, { "CommentId", remote }, { "Number", number }, { "Title", title } };
        }
        public Task<string> Download(Dictionary<string, object> episode) { return Download(episode, CancellationToken.None); }
        public async Task<string> Download(Dictionary<string, object> episode, CancellationToken cancellation)
        {
            var p = GetProvider(episode); string remote = Uri.EscapeDataString(Json.Text(episode, "CommentId"));
            string content;
            if (p.Id == "jellyfin")
            {
                string site = Json.Text(episode, "SiteId");
                if (!Regex.IsMatch(site, "^[a-z0-9_-]+$")) throw new InvalidDataException("来源标识无效");
                content = await api.Request("GET", "api/" + site + "/danmu/" + remote + "/download", null, true, cancellation);
            }
            else if (p.Id == "animeko")
            {
                var result = Json.Object(await Fetch(p, "/v1/danmaku/" + remote, null, cancellation));
                content = CommentsToXml(Json.Array(result, "DanmakuList"), "animeko");
            }
            else if (p.Id == "bahamut")
            {
                var result = Json.Object(await Fetch(p, "/anime/v1/danmu.php?geo=TW%2CHK&videoSn=" + remote, null, cancellation)); EnsureSuccess(result);
                content = CommentsToXml(Json.Array(Json.Child(result, "Data"), "Danmu"), "bahamut");
            }
            else
            {
                // Request JSON: official dandan redirects XML to related sources; JSON is uniform.
                string animeName = Json.Text(episode, "AnimeTitle");
                if (String.IsNullOrEmpty(animeName)) { lock (sync) { Dictionary<string, object> known; if (animes.TryGetValue(Json.Text(episode, "AnimeId"), out known)) animeName = Json.Text(known, "Name"); } }
                var result = Json.Object(await Fetch(p, "/api/v2/comment/" + remote + "?format=json&withRelated=true", null, cancellation, animeName)); EnsureSuccess(result);
                content = CommentsToXml(Json.Array(result, "Comments"), "dandan");
            }
            double shift; Double.TryParse(Json.Text(episode, "Shift"), NumberStyles.Float, CultureInfo.InvariantCulture, out shift);
            if (shift != 0 && !Double.IsNaN(shift) && !Double.IsInfinity(shift))
            {
                var document = ParseXml(content);
                foreach (XmlElement row in document.GetElementsByTagName("d")) { var fields = row.GetAttribute("p").Split(','); double time; if (fields.Length > 0 && Double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out time)) { fields[0] = Math.Max(0, time + shift).ToString("0.###", CultureInfo.InvariantCulture); row.SetAttribute("p", String.Join(",", fields)); } }
                content = document.OuterXml;
            }
            cancellation.ThrowIfCancellationRequested(); return SortXmlForPlayback(content);
        }
        public static string CommentsToXml(object[] rows, string source)
        {
            var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml("<i />");
            foreach (Dictionary<string, object> row in rows)
            {
                string text, id = First(row, "Id", "Sn", "Cid"), mode = "1", color = "16777215";
                double time;
                if (source == "animeko")
                {
                    var info = Json.Child(row, "DanmakuInfo"); if (info == null) continue;
                    if (!Double.TryParse(Json.Text(info, "PlayTime"), NumberStyles.Float, CultureInfo.InvariantCulture, out time)) continue;
                    time /= 1000; text = Json.Text(info, "Text");
                    string position = Json.Text(info, "Location"); mode = position == "TOP" ? "5" : position == "BOTTOM" ? "4" : "1";
                    long c; if (Int64.TryParse(Json.Text(info, "Color"), out c) && c >= 0) color = (c & 0xFFFFFF).ToString(CultureInfo.InvariantCulture);
                }
                else if (source == "bahamut")
                {
                    if (!Double.TryParse(Json.Text(row, "Time"), NumberStyles.Float, CultureInfo.InvariantCulture, out time)) continue;
                    time /= 10; text = Json.Text(row, "Text");
                    string position = Json.Text(row, "Position"); mode = position == "1" ? "5" : position == "2" ? "4" : "1";
                    int c; if (Int32.TryParse(Json.Text(row, "Color").TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c)) color = (c & 0xFFFFFF).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    var fields = Json.Text(row, "P").Split(',');
                    if (fields.Length < 3 || !Double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out time)) continue;
                    mode = fields[1]; color = fields[2]; text = First(row, "M", "Text");
                }
                text = CleanXmlText(text);
                int parsedMode, parsedColor;
                if (Double.IsNaN(time) || Double.IsInfinity(time) || time < 0 || !Int32.TryParse(mode, out parsedMode) || parsedMode < 1 || parsedMode > 6 || !Int32.TryParse(color, out parsedColor) || parsedColor < 0 || parsedColor > 16777215 || String.IsNullOrWhiteSpace(text)) continue;
                var d = xml.CreateElement("d");
                d.SetAttribute("p", time.ToString("0.###", CultureInfo.InvariantCulture) + "," + parsedMode + ",25," + parsedColor + ",0,0," + source + "," + Regex.Replace(id, "[^a-zA-Z0-9_-]", ""));
                d.InnerText = text; xml.DocumentElement.AppendChild(d);
            }
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize })) xml.WriteTo(writer);
            return output.ToString();
        }
        // Remote JSON can contain XML 1.0 control characters or broken UTF-16.
        // Keep valid surrogate pairs (emoji), CJK, tabs and line breaks intact.
        static string CleanXmlText(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            var clean = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1])) { clean.Append(c); clean.Append(value[++i]); }
                else if (!Char.IsSurrogate(c) && XmlConvert.IsXmlChar(c)) clean.Append(c);
            }
            return clean.ToString();
        }
        public static XmlDocument ParseXml(string content)
        {
            var xml = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml.Load(reader);
            if (xml.DocumentElement == null || xml.DocumentElement.Name != "i") throw new InvalidDataException("来源返回了无效弹幕 XML");
            return xml;
        }
        public static string SortXmlForPlayback(string content)
        {
            var xml = ParseXml(content);
            var rows = xml.DocumentElement.ChildNodes.OfType<XmlElement>().Where(x => x.Name == "d").ToArray();
            var times = new Dictionary<XmlElement, double>();
            bool ordered = true; double previous = -1;
            foreach (var row in rows)
            {
                double time;
                if (!Double.TryParse(row.GetAttribute("p").Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out time) || Double.IsNaN(time) || Double.IsInfinity(time) || time < 0)
                    throw new InvalidDataException("弹幕时间无效，原文件未更改。请尝试其他来源。");
                times[row] = time;
                if (time < previous) ordered = false;
                previous = time;
            }
            if (ordered) return content;
            // Stable ordering preserves equal-time comments, text and all attributes.
            // This helps clients seek by time; screen positions remain client-owned.
            var sorted = rows.OrderBy(x => times[x]).Select(x => x.CloneNode(true)).ToArray();
            for (int i = 0; i < rows.Length; i++) xml.DocumentElement.ReplaceChild(sorted[i], rows[i]);
            return xml.OuterXml;
        }
        static object ApiAnime(Dictionary<string, object> a, object[] eps = null)
        {
            int count; Int32.TryParse(Json.Text(a, "EpisodeSize"), out count);
            string startDate = Json.Text(a, "StartDate"); DateTime date;
            if (startDate.Length == 4) startDate += "-01-01";
            startDate = DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture) : "1970-01-01T00:00:00Z";
            return new { animeId = Int64.Parse(Json.Text(a, "Id")), bangumiId = Json.Text(a, "Id"), animeTitle = Json.Text(a, "Name") + " from " + Json.Text(a, "Site"), imageUrl = "", type = Json.Text(a, "Category"), typeDescription = Json.Text(a, "Category"), startDate = startDate, episodeCount = eps == null ? count : eps.Length, episodes = eps == null ? null : eps.Cast<Dictionary<string, object>>().Select(e => (object)new { episodeId = Int64.Parse(Json.Text(e, "Id")), episodeNumber = Json.Text(e, "Number"), episodeTitle = Json.Text(e, "Title"), airDate = "1970-01-01T00:00:00Z" }).ToArray() };
        }
        public async Task<CatalogReply> Route(string route)
        {
            var uri = new Uri("http://localhost" + route); string path = uri.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (path == "/api/v2/search/anime" || path == "/api/v2/search/episodes")
            {
                var result = await Search(query[path.EndsWith("episodes") ? "anime" : "keyword"] ?? "", settings.AnimeOnly);
                var tasks = result.Items.Take(path.EndsWith("episodes") ? 15 : 200).Cast<Dictionary<string, object>>().Select(async a =>
                {
                    object[] eps = null;
                    if (path.EndsWith("episodes")) { try { eps = await Episodes(a); } catch { eps = new object[0]; } }
                    return ApiAnime(a, eps);
                });
                var list = await Task.WhenAll(tasks);
                bool success = result.Sources.Cast<Dictionary<string, object>>().Any(x => Json.Text(x, "Error") == "");
                return new CatalogReply { Content = Json.Write(new { success = success, animes = list, sourceStatus = result.Sources }) };
            }
            string id = path.Substring(path.LastIndexOf('/') + 1);
            Dictionary<string, object> item = null;
            lock (sync)
            {
                if (path.StartsWith("/api/v2/bangumi/")) animes.TryGetValue(id, out item);
                else if (path.StartsWith("/api/v2/comment/")) episodes.TryGetValue(id, out item);
            }
            if (item == null) return null; // Retain compatibility with previously cached Jellyfin IDs.
            if (path.StartsWith("/api/v2/bangumi/"))
            {
                var eps = await Episodes(item);
                return new CatalogReply { Content = Json.Write(new { success = true, bangumi = ApiAnime(item, eps) }) };
            }
            string content = await Download(item);
            if (String.Equals(query["format"], "xml", StringComparison.OrdinalIgnoreCase)) return new CatalogReply { ContentType = "text/xml; charset=utf-8", Content = content };
            var comments = new List<object>();
            foreach (XmlElement d in ParseXml(content).GetElementsByTagName("d"))
            {
                var fields = d.GetAttribute("p").Split(','); if (fields.Length < 4) continue;
                comments.Add(new { cid = comments.Count + 1, p = fields[0] + "," + fields[1] + "," + fields[3] + "," + (fields.Length > 6 ? fields[6] : "0"), m = d.InnerText });
            }
            return new CatalogReply { Content = Json.Write(new { count = comments.Count, comments = comments }) };
        }
        void SaveCache()
        {
            while (animes.Count > 1000) animes.Remove(animes.Keys.First());
            while (episodes.Count > 5000) episodes.Remove(episodes.Keys.First());
            try { SettingsStore.AtomicWrite(CachePath, Json.Write(new { Animes = animes.Values.ToArray(), Episodes = episodes.Values.ToArray() })); }
            catch { Log.Write("无法保存动漫搜索缓存；当前查询仍可使用。"); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength, StringBuilder dest, int destLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);
        public static string Chinese(string value, bool traditional)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";
            var dest = new StringBuilder(value.Length * 2 + 2);
            return LCMapStringEx("zh-CN", traditional ? 0x04000000u : 0x02000000u, value, -1, dest, dest.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0 ? dest.ToString() : value;
        }
        public void Dispose() { http.Dispose(); }
    }
}
