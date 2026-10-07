using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class CacheRetention
    {
        public static readonly int[] Months = { 1, 3, 6, 12, 0 };
        public static readonly string[] Labels = { "1 个月", "3 个月", "半年", "1 年", "长期" };
        public static bool Valid(int months) { return Months.Contains(months); }
        public static DateTime Expiry(DateTime createdUtc, int months)
        {
            if (!Valid(months)) throw new ArgumentException("缓存有效期无效。");
            if (months == 0) return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            try { return createdUtc.AddMonths(months); } catch (ArgumentOutOfRangeException) { return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc); }
        }
    }
    public sealed class ApiCacheEntry
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string Anime { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string Content { get; set; }
        public long Bytes { get; set; }
        public string TypeLabel { get { return Kind == "hash" ? "文件特征" : Kind == "match" ? "文件识别" : Kind == "comment" ? "弹幕" : "搜索 / 作品详情"; } }
        public string SizeLabel { get { return (Bytes / 1000000.0).ToString("N2") + " MB"; } }
        public string CreatedLabel { get { return CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
        public string ExpiresLabel { get { return ExpiresUtc == DateTime.MaxValue ? "长期" : ExpiresUtc <= DateTime.UtcNow ? "已过期" : ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
    }
    // Successful responses only. Credentials and signatures never enter cache keys or data.
    public sealed class DandanApiCache
    {
        readonly string directory;
        readonly AppSettings settings;
        readonly object sync = new object();
        readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        long generation;
        public string DirectoryPath { get { return directory; } }
        public int RetentionMonths { get { return settings.CacheRetentionMonths; } }
        public DandanApiCache(AppSettings settings) { this.settings = settings; directory = Path.Combine(Paths.Data, "dandan-cache"); }
        public void SetRetention(int months)
        {
            if (!CacheRetention.Valid(months)) throw new ArgumentException("缓存有效期无效。");
            lock (sync)
            {
                int previous = settings.CacheRetentionMonths; settings.CacheRetentionMonths = months;
                try { SettingsStore.Save(settings); } catch { settings.CacheRetentionMonths = previous; throw; }
            }
        }
        public static string Key(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        string FilePath(string key)
        { if (key == null || key.Length != 64 || key.Any(x => !Uri.IsHexDigit(x))) throw new ArgumentException("缓存编号无效。"); return Files().FirstOrDefault(x => Path.GetFileName(x) == key + ".json") ?? Path.Combine(directory, key + ".json"); }
        string[] Files()
        {
            if (!Directory.Exists(directory)) return new string[0];
            return Directory.GetFiles(directory, "*.json").Concat(Directory.GetDirectories(directory).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).SelectMany(x => Directory.GetFiles(x, "*.json"))).ToArray();
        }
        public static string GroupName(string kind, string label, string content, string anime = null)
        {
            try
            {
                var response = Json.Object(content);
                var detail = Json.Child(response, "Bangumi");
                string name = Json.Text(detail, "AnimeTitle"); if (!String.IsNullOrWhiteSpace(name)) return name;
                var matches = Json.Array(response, "Matches").OfType<Dictionary<string, object>>().Select(x => Json.Text(x, "AnimeTitle")).Where(x => x != "").Distinct().ToArray();
                if (matches.Length == 1) return matches[0];
            }
            catch { }
            return !String.IsNullOrWhiteSpace(anime) ? anime : kind == "search" && !String.IsNullOrWhiteSpace(label) && !label.StartsWith("/") ? label : "未分类缓存";
        }
        string GroupPath(string anime)
        {
            string safe = new string(anime.Select(x => Path.GetInvalidFileNameChars().Contains(x) || Char.IsControl(x) ? '_' : x).ToArray()).Trim(' ', '.');
            if (safe.Length > 80) safe = safe.Substring(0, 80); if (safe.Length == 0) safe = "缓存";
            return Path.Combine(directory, safe + " · " + Key(anime).Substring(0, 8));
        }
        string Migrate(string path, ApiCacheEntry entry)
        {
            entry.Anime = GroupName(entry.Kind, entry.Label, entry.Content, entry.Anime);
            string group = GroupPath(entry.Anime), destination = Path.Combine(group, entry.Key + ".json");
            if (!String.Equals(path, destination, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(group); SettingsStore.AtomicWrite(destination, Json.Write(entry), false); File.Delete(path);
            }
            return destination;
        }
        public string Read(string key)
        {
            lock (sync)
            {
                try { string path = FilePath(key); if (!File.Exists(path)) return null; var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(path)); if (entry == null || entry.Key != key) return null; Migrate(path, entry); return CacheRetention.Expiry(entry.CreatedUtc, RetentionMonths) > DateTime.UtcNow ? entry.Content : null; }
                catch { return null; }
            }
        }
        public void Write(string key, string kind, string label, string content, string anime = null)
        {
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    DateTime created = DateTime.UtcNow;
                    string old = FilePath(key); var entry = new ApiCacheEntry { Key = key, Kind = kind, Label = label, Anime = GroupName(kind, label, content, anime), Content = content, CreatedUtc = created, ExpiresUtc = CacheRetention.Expiry(created, RetentionMonths) };
                    string group = GroupPath(entry.Anime); Directory.CreateDirectory(group); string destination = Path.Combine(group, key + ".json");
                    SettingsStore.AtomicWrite(destination, Json.Write(entry), false); if (old != destination && File.Exists(old)) File.Delete(old);
                    var files = Files().Select(x => new FileInfo(x)).OrderBy(x => x.LastWriteTimeUtc).ToArray();
                    long total = files.Sum(x => x.Length); int count = files.Length;
                    foreach (var file in files) { if (count <= 5000 && total <= 256L * 1024 * 1024) break; total -= file.Length; count--; file.Delete(); }
                }
                catch { Log.Write("官方接口缓存写入失败，本次数据仍可使用。"); }
            }
        }
        public async Task<string> Get(string key, string kind, string label, Func<Task<string>> fetch, CancellationToken cancellation, string anime = null)
        {
            cancellation.ThrowIfCancellationRequested();
            string cached = Read(key); if (cached != null) { SetAnime(key, anime); return cached; }
            await requestGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                cached = Read(key); if (cached != null) { SetAnime(key, anime); return cached; }
                long before; lock (sync) before = generation;
                string content = await fetch().ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
                lock (sync) { if (before == generation) Write(key, kind, label, content, anime); }
                return content;
            }
            finally { requestGate.Release(); }
        }
        public ApiCacheEntry[] Entries()
        {
            lock (sync)
            {
                if (!Directory.Exists(directory)) return new ApiCacheEntry[0];
                var result = new List<ApiCacheEntry>();
                foreach (var file in Files())
                    try { var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(file)); if (entry != null && Path.GetFileName(FilePath(entry.Key)) == Path.GetFileName(file)) { string path = Migrate(file, entry); entry.ExpiresUtc = CacheRetention.Expiry(entry.CreatedUtc, RetentionMonths); entry.Bytes = new FileInfo(path).Length; entry.Content = null; result.Add(entry); } } catch { }
                return result.OrderByDescending(x => x.CreatedUtc).ToArray();
            }
        }
        public void Remove(IEnumerable<string> keys)
        { lock (sync) { generation++; foreach (string key in keys.ToArray()) { string path = FilePath(key); if (File.Exists(path)) File.Delete(path); } RemoveEmptyFolders(); } }
        void RemoveEmptyFolders() { if (Directory.Exists(directory)) foreach (string folder in Directory.GetDirectories(directory).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0)) if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        public void RemoveAnime(string anime) { lock (sync) Remove(Entries().Where(x => x.Anime == anime).Select(x => x.Key)); }
        public void SetAnime(string key, string anime)
        {
            if (String.IsNullOrWhiteSpace(anime)) return;
            lock (sync) { string path = FilePath(key); if (!File.Exists(path)) return; var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(path)); entry.Anime = anime; Migrate(path, entry); }
        }
        public void Clear(bool expiredOnly)
        {
            lock (sync)
            {
                if (expiredOnly) { Remove(Entries().Where(x => x.ExpiresUtc <= DateTime.UtcNow).Select(x => x.Key)); return; }
                generation++; if (!Directory.Exists(directory)) return;
                foreach (var file in Files()) if (Path.GetFileName(file).Length == 69 && Path.GetFileName(file).Substring(0, 64).All(Uri.IsHexDigit)) File.Delete(file);
                RemoveEmptyFolders();
            }
        }
    }
}
