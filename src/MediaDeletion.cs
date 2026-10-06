using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DanmuCinema
{
    public enum MediaDeleteKind { Folder, Video, Danmu }
    public sealed class MediaDeleteTarget
    {
        public string Path;
        public bool Directory;
        public long Length, WriteTicks;
    }
    public sealed class MediaDeletePlan
    {
        public string Root;
        public MediaDeleteKind Kind;
        public MediaDeleteTarget[] Targets;
        public string Title { get { return Kind == MediaDeleteKind.Folder ? "删除整个文件夹" : Kind == MediaDeleteKind.Video ? "删除视频" : "删除弹幕"; } }
        public string Confirmation
        {
            get
            {
                string scope = Kind == MediaDeleteKind.Folder ? "文件夹内的全部内容，包括视频、弹幕和其他文件" : Kind == MediaDeleteKind.Video ? "视频文件；同名弹幕和其他文件会保留" : "同名 XML 弹幕；视频文件会保留";
                return "将永久删除 " + Targets.Length + " 个目标，不能撤销。\n删除范围：" + scope + "。\n\n" + String.Join("\n", Targets.Select(x => x.Path));
            }
        }
        public bool Affects(string video)
        {
            if (String.IsNullOrWhiteSpace(video) || !System.IO.Path.IsPathRooted(video)) return false;
            string path = MediaDeletion.FullPath(video);
            return Targets.Any(x => x.Directory ? MediaDeletion.Within(path, x.Path) : String.Equals(path, Kind == MediaDeleteKind.Danmu ? System.IO.Path.ChangeExtension(x.Path, System.IO.Path.GetExtension(video)) : x.Path, StringComparison.OrdinalIgnoreCase));
        }
    }
    // Only explicit local media targets can be removed. Never accept a library root or a link traversal.
    public static class MediaDeletion
    {
        public static string FullPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("删除目标缺少有效的本地路径。");
            string full = Path.GetFullPath(path);
            return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        public static bool Within(string path, string directory)
        { return path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        static void CheckPath(string path, string root)
        {
            if (!Within(path, root)) throw new InvalidOperationException("只能删除已配置媒体目录内的内容，不能删除媒体库根目录。");
            for (string current = path; !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("目标含目录链接或符号链接，已取消删除。");
                if (String.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        static void CheckTree(string path)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("文件夹包含目录链接或符号链接，已取消删除。");
                if ((entry.Attributes & FileAttributes.Directory) != 0) CheckTree(entry.FullName);
            }
        }
        public static MediaDeletePlan Plan(IEnumerable<LibraryEntry> entries, MediaDeleteKind kind, string mediaRoot)
        {
            string root = FullPath(mediaRoot); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (kind == MediaDeleteKind.Folder)
                { paths.Add(FullPath(entry.IsFolder ? entry.FolderPath : Path.GetDirectoryName(FullPath(Json.Text(entry.Item, "Path"))))); continue; }
                foreach (var video in entry.IsFolder ? entry.Members : new[] { entry })
                {
                    string source = FullPath(Json.Text(video.Item, "Path")); CheckPath(source, root);
                    paths.Add(kind == MediaDeleteKind.Danmu ? Path.ChangeExtension(source, ".xml") : source);
                }
            }
            var targets = new List<MediaDeleteTarget>();
            foreach (string path in paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                CheckPath(path, root);
                if (kind == MediaDeleteKind.Folder)
                {
                    if (!Directory.Exists(path)) continue; CheckTree(path);
                    // An ancestor folder already covers nested selected folders.
                    if (paths.Any(parent => !String.Equals(parent, path, StringComparison.OrdinalIgnoreCase) && Within(path, parent))) continue;
                    targets.Add(new MediaDeleteTarget { Path = path, Directory = true, WriteTicks = Directory.GetLastWriteTimeUtc(path).Ticks });
                }
                else if (File.Exists(path))
                { var file = new FileInfo(path); targets.Add(new MediaDeleteTarget { Path = path, Length = file.Length, WriteTicks = file.LastWriteTimeUtc.Ticks }); }
            }
            if (targets.Count == 0) throw new InvalidOperationException(kind == MediaDeleteKind.Danmu ? "所选影片没有本地 XML 弹幕。" : "删除目标已不存在，请刷新列表。");
            return new MediaDeletePlan { Root = root, Kind = kind, Targets = targets.ToArray() };
        }
        public static void Execute(MediaDeletePlan plan, CancellationToken cancellation)
        {
            // Revalidate every target after confirmation, before removing any of them.
            foreach (var target in plan.Targets)
            {
                cancellation.ThrowIfCancellationRequested(); CheckPath(target.Path, plan.Root);
                if (target.Directory)
                {
                    if (!Directory.Exists(target.Path) || Directory.GetLastWriteTimeUtc(target.Path).Ticks != target.WriteTicks) throw new IOException("文件夹在确认期间发生变化，请重新操作。");
                    CheckTree(target.Path);
                }
                else
                {
                    var file = new FileInfo(target.Path);
                    if (!file.Exists || file.Length != target.Length || file.LastWriteTimeUtc.Ticks != target.WriteTicks) throw new IOException("文件在确认期间发生变化，请重新操作。");
                }
            }
            foreach (var target in plan.Targets)
            {
                cancellation.ThrowIfCancellationRequested(); CheckPath(target.Path, plan.Root);
                if (target.Directory) { CheckTree(target.Path); Directory.Delete(target.Path, true); }
                else File.Delete(target.Path);
            }
        }
    }
}
