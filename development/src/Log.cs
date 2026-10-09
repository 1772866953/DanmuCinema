using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace DanmuCinema
{
    public sealed class LogClearResult
    {
        public int Deleted, Pending;
        public string Message { get { return "已删除 " + Deleted + " 个日志文件。" + (Pending == 0 ? "" : "另有 " + Pending + " 个日志正在使用，将在服务停止后自动删除。"); } }
    }
    public static class Log
    {
        static readonly object Sync = new object();
        static string policyRoot;
        static int retentionDays = 30;
        static DateTime nextCleanup;
        static long generation;
        public static long Generation { get { return Interlocked.Read(ref generation); } }
        public static event Action<string> Added;
        public static event Action Reset;
        static string PendingPath { get { return Path.Combine(Paths.Data, "log-cleanup-pending.json"); } }
        public static void Configure(int days)
        {
            if (days < 1 || days > 3650) throw new ArgumentOutOfRangeException("days");
            lock (Sync) { policyRoot = Paths.Root; retentionDays = days; nextCleanup = DateTime.MinValue; }
            CleanupIfDue();
        }
        public static void CleanupIfDue()
        {
            bool changed = false;
            lock (Sync)
            {
                if (policyRoot != Paths.Root) { policyRoot = Paths.Root; retentionDays = 30; nextCleanup = DateTime.MinValue; }
                if (DateTime.Now < nextCleanup) return;
                nextCleanup = DateTime.Now.AddHours(1);
                try { CleanupPendingCore(); changed = PruneCore(DateTime.Now); } catch { }
                if (changed) Interlocked.Increment(ref generation);
            }
            if (changed) RaiseReset();
        }
        internal static bool Prune(DateTime now)
        { bool changed; lock (Sync) { changed = PruneCore(now); if (changed) Interlocked.Increment(ref generation); } if (changed) RaiseReset(); return changed; }
        static bool PruneCore(DateTime now)
        {
            DateTime cutoff = now.AddDays(-retentionDays); bool changed = false;
            foreach (var file in Files())
            {
                try
                {
                    if (file == Paths.LogPath || file == Paths.LogPath + ".1")
                    {
                        var kept = new List<string>(); bool keep = File.GetLastWriteTime(file) >= cutoff;
                        foreach (var line in File.ReadLines(file))
                        {
                            DateTime stamp;
                            if (line.Length >= 19 && DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp)) keep = stamp >= cutoff;
                            if (keep) kept.Add(line);
                        }
                        var original = File.ReadAllLines(file);
                        if (kept.Count == original.Length) continue;
                        if (kept.Count == 0) File.Delete(file); else SettingsStore.AtomicWrite(file, String.Join(Environment.NewLine, kept) + Environment.NewLine, false);
                        changed = true;
                    }
                    else if (File.GetLastWriteTime(file) < cutoff) { File.Delete(file); changed = true; }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return changed;
        }
        static string[] Files()
        {
            var files = new List<string>();
            if (File.Exists(Paths.LogPath)) files.Add(Paths.LogPath);
            if (File.Exists(Paths.LogPath + ".1")) files.Add(Paths.LogPath + ".1");
            string server = Path.Combine(Paths.Data, "server-logs");
            if (Directory.Exists(server) && (File.GetAttributes(server) & FileAttributes.ReparsePoint) == 0)
                AddServerLogs(server, files);
            return files.Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        static void AddServerLogs(string directory, List<string> files)
        {
            foreach (string file in Directory.GetFiles(directory)) if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) files.Add(file);
            foreach (string child in Directory.GetDirectories(directory)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) AddServerLogs(child, files);
        }
        public static LogClearResult ClearAll()
        {
            var result = new LogClearResult(); var pending = new List<string>();
            lock (Sync)
            {
                foreach (var file in Files())
                {
                    try { File.Delete(file); result.Deleted++; }
                    catch (IOException) { pending.Add(file.Substring(Paths.Data.Length + 1)); }
                    catch (UnauthorizedAccessException) { pending.Add(file.Substring(Paths.Data.Length + 1)); }
                }
                StorePending(pending); result.Pending = pending.Count;
                Interlocked.Increment(ref generation);
            }
            RaiseReset(); return result;
        }
        static void StorePending(IEnumerable<string> pending)
        {
            var list = pending.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (list.Length == 0) { if (File.Exists(PendingPath)) File.Delete(PendingPath); }
            else { Directory.CreateDirectory(Paths.Data); SettingsStore.AtomicWrite(PendingPath, Json.Write(list), false); }
        }
        public static void CleanupPending() { lock (Sync) { try { CleanupPendingCore(); } catch { } } }
        static void CleanupPendingCore()
        {
            if (!File.Exists(PendingPath)) return;
            var known = new HashSet<string>(Files(), StringComparer.OrdinalIgnoreCase); var remaining = new List<string>();
            foreach (var relative in Json.Read<string[]>(File.ReadAllText(PendingPath)))
            {
                if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) continue;
                string path = Path.GetFullPath(Path.Combine(Paths.Data, relative));
                if (!path.StartsWith(Paths.Data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !known.Contains(path)) continue;
                try { File.Delete(path); }
                catch (IOException) { remaining.Add(relative); }
                catch (UnauthorizedAccessException) { remaining.Add(relative); }
            }
            StorePending(remaining);
        }
        static void RaiseReset() { var handler = Reset; if (handler != null) handler(); }
        public static string RecentText()
        {
            lock (Sync)
            {
                try { return File.Exists(Paths.LogPath) ? String.Join(Environment.NewLine, File.ReadLines(Paths.LogPath).Reverse().Take(160).Reverse()) : ""; }
                catch (IOException) { return ""; }
            }
        }
        public static void Write(string message)
        {
            CleanupIfDue();
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Paths.Data);
                    if (File.Exists(Paths.LogPath) && new FileInfo(Paths.LogPath).Length > 3 * 1024 * 1024)
                    {
                        var archive = Paths.LogPath + ".1"; if (File.Exists(archive)) File.Delete(archive); File.Move(Paths.LogPath, archive);
                    }
                    File.AppendAllText(Paths.LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
            var handler = Added; if (handler != null) handler(line);
        }
        // Keep the server's own cleanup policy aligned, without touching activity records or credentials.
        public static void ConfigureServerRetention(int days)
        {
            if (days < 1 || days > 3650) throw new ArgumentOutOfRangeException("days");
            try { ConfigureServerRetentionCore(days); }
            catch (Exception) { Write("服务器日志保留设置暂未同步，下次启动服务时会重试。"); }
        }
        static void ConfigureServerRetentionCore(int days)
        {
            string config = Path.Combine(Paths.ServerData, "config");
            string system = Path.Combine(config, "system.xml");
            if (File.Exists(system))
            {
                var doc = new XmlDocument { XmlResolver = null }; doc.Load(system);
                var node = doc.DocumentElement.SelectSingleNode("LogFileRetentionDays");
                if (node == null) { node = doc.CreateElement("LogFileRetentionDays"); doc.DocumentElement.AppendChild(node); }
                if (node.InnerText != days.ToString(CultureInfo.InvariantCulture)) { node.InnerText = days.ToString(CultureInfo.InvariantCulture); SettingsStore.AtomicWrite(system, doc.OuterXml, false); }
            }
            foreach (string name in new[] { "logging.default.json", "logging.json" })
            {
                string path = Path.Combine(config, name); if (!File.Exists(path)) continue;
                var json = Json.Object(File.ReadAllText(path)); if (RemoveCountLimit(json)) SettingsStore.AtomicWrite(path, Json.Write(json), false);
            }
        }
        static bool RemoveCountLimit(object value)
        {
            bool changed = false; var dict = value as Dictionary<string, object>;
            if (dict != null)
            {
                var args = Json.Child(dict, "Args");
                if (Json.Text(dict, "Name") == "File" && args != null)
                {
                    string path = Json.Text(args, "path");
                    if (path.Contains("%JELLYFIN_LOG_DIR%") || path.StartsWith(Path.Combine(Paths.Data, "server-logs"), StringComparison.OrdinalIgnoreCase))
                    { object count; if (!args.TryGetValue("retainedFileCountLimit", out count) || count != null) { args["retainedFileCountLimit"] = null; changed = true; } }
                }
                foreach (var child in dict.Values.ToArray()) changed |= RemoveCountLimit(child);
            }
            else { var items = value as System.Collections.IEnumerable; if (items != null && !(value is string)) foreach (var item in items) changed |= RemoveCountLimit(item); }
            return changed;
        }
    }
}
