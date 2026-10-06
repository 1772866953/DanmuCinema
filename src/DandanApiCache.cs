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
    public sealed class ApiCacheEntry
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string Content { get; set; }
        public long Bytes { get; set; }
        public string TypeLabel { get { return Kind == "hash" ? "文件特征" : Kind == "match" ? "文件识别" : Kind == "comment" ? "弹幕" : "搜索 / 作品详情"; } }
        public string SizeLabel { get { return (Bytes / 1000000.0).ToString("N2") + " MB"; } }
        public string CreatedLabel { get { return CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
        public string ExpiresLabel { get { return ExpiresUtc <= DateTime.UtcNow ? "已过期" : ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
    }
    // Successful responses only. Credentials and signatures never enter cache keys or data.
    public sealed class DandanApiCache
    {
        readonly string directory;
        readonly object sync = new object();
        readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        long generation;
        public string DirectoryPath { get { return directory; } }
        public DandanApiCache() { directory = Path.Combine(Paths.Data, "dandan-cache"); }
        public static string Key(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        string FilePath(string key)
        { if (key == null || key.Length != 64 || key.Any(x => !Uri.IsHexDigit(x))) throw new ArgumentException("缓存编号无效。"); return Path.Combine(directory, key + ".json"); }
        public string Read(string key)
        {
            lock (sync)
            {
                try { string path = FilePath(key); if (!File.Exists(path)) return null; var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(path)); return entry != null && entry.Key == key && entry.ExpiresUtc > DateTime.UtcNow ? entry.Content : null; }
                catch { return null; }
            }
        }
        public void Write(string key, string kind, string label, string content, TimeSpan lifetime)
        {
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    SettingsStore.AtomicWrite(FilePath(key), Json.Write(new ApiCacheEntry { Key = key, Kind = kind, Label = label, Content = content, CreatedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.Add(lifetime) }), false);
                    var files = new DirectoryInfo(directory).GetFiles("*.json").OrderBy(x => x.LastWriteTimeUtc).ToArray();
                    long total = files.Sum(x => x.Length); int count = files.Length;
                    foreach (var file in files) { if (count <= 5000 && total <= 256L * 1024 * 1024) break; total -= file.Length; count--; file.Delete(); }
                }
                catch { Log.Write("官方接口缓存写入失败，本次数据仍可使用。"); }
            }
        }
        public async Task<string> Get(string key, string kind, string label, TimeSpan lifetime, Func<Task<string>> fetch, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string cached = Read(key); if (cached != null) return cached;
            await requestGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                cached = Read(key); if (cached != null) return cached;
                long before; lock (sync) before = generation;
                string content = await fetch().ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
                lock (sync) { if (before == generation) Write(key, kind, label, content, lifetime); }
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
                foreach (var file in new DirectoryInfo(directory).GetFiles("*.json"))
                    try { var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(file.FullName)); if (entry != null && FilePath(entry.Key) == file.FullName) { entry.Bytes = file.Length; entry.Content = null; result.Add(entry); } } catch { }
                return result.OrderByDescending(x => x.CreatedUtc).ToArray();
            }
        }
        public void Remove(IEnumerable<string> keys)
        { lock (sync) { generation++; foreach (string key in keys) { string path = FilePath(key); if (File.Exists(path)) File.Delete(path); } } }
        public void Clear(bool expiredOnly)
        {
            lock (sync)
            {
                if (expiredOnly) { Remove(Entries().Where(x => x.ExpiresUtc <= DateTime.UtcNow).Select(x => x.Key)); return; }
                generation++; if (!Directory.Exists(directory)) return;
                foreach (var file in new DirectoryInfo(directory).GetFiles("*.json")) if (file.Name.Length == 69 && file.Name.Substring(0, 64).All(Uri.IsHexDigit)) file.Delete();
            }
        }
    }
}
