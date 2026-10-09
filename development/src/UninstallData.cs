using System;
using System.IO;
using System.Threading;

namespace DanmuCinema
{
    public static class UninstallData
    {
        public static void Remove(bool keepCacheAndLogs)
        {
            string root = Path.GetFullPath(Paths.Root);
            string data = Path.Combine(root, "data");
            string media = null;
            try { media = Json.Text(Json.Object(File.ReadAllText(Paths.SettingsFile)), "MediaFolder"); if (!String.IsNullOrWhiteSpace(media)) media = Path.GetFullPath(media).TrimEnd(Path.DirectorySeparatorChar); }
            catch { }
            if (Directory.Exists(data))
            {
                if ((File.GetAttributes(data) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("数据目录是链接，无法安全清理。");
                if (!keepCacheAndLogs) DeleteTree(data, root, media);
                else
                {
                    foreach (string directory in Directory.GetDirectories(data))
                    {
                        string name = Path.GetFileName(directory);
                        if (name != "cache" && name != "dandan-cache" && name != "server-logs") DeleteTree(directory, root, media);
                    }
                    foreach (string file in Directory.GetFiles(data))
                    {
                        string name = Path.GetFileName(file);
                        if (!name.StartsWith("controller.log", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("anime-catalog-cache.json", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("log-cleanup-pending.json", StringComparison.OrdinalIgnoreCase) && !IsMedia(file, media)) DeleteFile(file);
                    }
                }
            }
            DeleteTree(Path.Combine(root, "config"), root, media);
        }
        static bool IsMedia(string path, string media)
        {
            if (!String.IsNullOrWhiteSpace(media) && (String.Equals(path, media, StringComparison.OrdinalIgnoreCase) || path.StartsWith(media + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return true;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(new[] { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".m2ts", ".mts", ".flv", ".webm", ".mpg", ".mpeg", ".vob", ".iso", ".mxf", ".rmvb", ".rm", ".asf", ".3gp", ".m2v", ".ogv" }, extension) >= 0;
        }
        static void Retry(Action operation)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { operation(); return; }
                catch (IOException) { if (attempt == 5) throw; }
                catch (UnauthorizedAccessException) { if (attempt == 5) throw; }
                Thread.Sleep(150);
            }
        }
        static void DeleteFile(string path) { Retry(() => File.Delete(path)); }
        static void DeleteTree(string path, string root, string media)
        {
            path = Path.GetFullPath(path);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("缓存目录超出数据目录。");
            if (!Directory.Exists(path)) return;
            if (!String.IsNullOrWhiteSpace(media) && (String.Equals(path, media, StringComparison.OrdinalIgnoreCase) || path.StartsWith(media + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return;
            // Remove directory links themselves, never enumerate their targets.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (string file in Directory.GetFiles(path)) if (!IsMedia(file, media)) DeleteFile(file);
                foreach (string child in Directory.GetDirectories(path)) DeleteTree(child, root, media);
                if (Directory.GetFileSystemEntries(path).Length != 0) return;
            }
            Retry(() => Directory.Delete(path));
        }
    }
}
