using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DanmuCinema
{
    public static class CacheAndDeletionTests
    {
        public static void Run(List<string> report)
        {
            var settings = new AppSettings(); var cache = new DandanApiCache(settings); cache.Clear(false);
            var created = new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Utc);
            SelfTests.Assert(CacheRetention.Expiry(created, 1).Day == 29 && CacheRetention.Expiry(created, 3).Month == 4 && CacheRetention.Expiry(created, 6).Month == 7 && CacheRetention.Expiry(created, 12).Year == 2025, "统一缓存有效期按日历月计算，兼容月末和闰年", report);
            string key = DandanApiCache.Key("retention"); cache.Write(key, "comment", "保留规则测试", "response");
            var entry = Json.Read<ApiCacheEntry>(File.ReadAllText(Path.Combine(cache.DirectoryPath, key + ".json"))); entry.CreatedUtc = DateTime.UtcNow.AddMonths(-2); entry.ExpiresUtc = DateTime.UtcNow.AddYears(-1);
            File.WriteAllText(Path.Combine(cache.DirectoryPath, key + ".json"), Json.Write(entry));
            cache.SetRetention(1); SelfTests.Assert(cache.Read(key) == null && cache.Entries().Single().ExpiresUtc <= DateTime.UtcNow, "缩短有效期即时使已有旧缓存过期", report);
            cache.SetRetention(3); SelfTests.Assert(cache.Read(key) == "response" && SettingsStore.Load().CacheRetentionMonths == 3 && new DandanApiCache(SettingsStore.Load()).Read(key) == "response", "延长有效期复用已有缓存并在重启后保持", report);
            cache.SetRetention(0); cache.Clear(true); SelfTests.Assert(cache.Read(key) == "response" && cache.Entries().Single().ExpiresLabel == "长期", "长期缓存不因时间过期或被清理过期误删", report);
            foreach (string kind in new[] { "hash", "match", "search" }) cache.Write(DandanApiCache.Key(kind), kind, kind, "fixture");
            SelfTests.Assert(cache.Entries().All(x => x.ExpiresUtc == DateTime.MaxValue), "文件特征、识别、搜索和弹幕共用同一有效期", report);
            bool invalid = false; try { cache.SetRetention(2); } catch (ArgumentException) { invalid = true; }
            SelfTests.Assert(invalid && cache.RetentionMonths == 0, "拒绝非预设有效期，保留已保存设置", report);
            cache.Clear(false); cache.SetRetention(3);

            string root = Path.Combine(Paths.Root, "delete-fixtures"); string folder = Path.Combine(root, "番剧"); Directory.CreateDirectory(folder);
            string video = Path.Combine(folder, "episode.mkv"), xml = Path.ChangeExtension(video, ".xml"), extra = Path.Combine(folder, "poster.jpg");
            File.WriteAllText(video, "fixture video"); File.WriteAllText(xml, "fixture XML"); File.WriteAllText(extra, "fixture poster");
            var local = new LibraryEntry { Item = new Dictionary<string, object> { { "Id", "deletionfixture" }, { "Path", video } }, Key = "fixture", Name = "episode.mkv" };
            var folderEntry = new LibraryEntry { IsFolder = true, FolderPath = folder, Members = new[] { local } };
            var plan = MediaDeletion.Plan(new[] { local }, MediaDeleteKind.Danmu, root);
            SelfTests.Assert(plan.Targets.Length == 1 && plan.Affects(video) && plan.Confirmation.Contains("永久删除") && plan.Confirmation.Contains(xml), "删除弹幕预览列出精确 XML 路径与删除范围", report);
            MediaDeletion.Execute(plan, CancellationToken.None);
            SelfTests.Assert(!File.Exists(xml) && File.Exists(video) && File.Exists(extra), "删除弹幕只删除同名 XML，保留视频和其他文件", report);
            File.WriteAllText(xml, "fixture XML"); plan = MediaDeletion.Plan(new[] { local, local }, MediaDeleteKind.Video, root); MediaDeletion.Execute(plan, CancellationToken.None);
            SelfTests.Assert(plan.Targets.Length == 1 && !File.Exists(video) && File.Exists(xml) && File.Exists(extra), "多选重复目标去重，删除视频保留弹幕和其他文件", report);
            File.WriteAllText(video, "fixture video");
            plan = MediaDeletion.Plan(new[] { local }, MediaDeleteKind.Video, root); File.AppendAllText(video, "changed");
            invalid = false; try { MediaDeletion.Execute(plan, CancellationToken.None); } catch (IOException) { invalid = true; }
            SelfTests.Assert(invalid && File.Exists(video), "删除确认期间文件变化时中止并保留文件", report);
            var rootEntry = new LibraryEntry { IsFolder = true, FolderPath = root, Members = new[] { local } };
            invalid = false; try { MediaDeletion.Plan(new[] { rootEntry }, MediaDeleteKind.Folder, root); } catch (InvalidOperationException) { invalid = true; }
            SelfTests.Assert(invalid && Directory.Exists(root), "禁止删除媒体库根目录", report);
            invalid = false; try { MediaDeletion.Plan(new[] { folderEntry }, MediaDeleteKind.Folder, root + "-other"); } catch (InvalidOperationException) { invalid = true; }
            SelfTests.Assert(invalid && File.Exists(video), "删除边界按完整目录判断，不接受前缀相似的外部目录", report);
            string outside = Path.Combine(Paths.Root, "delete-outside"); Directory.CreateDirectory(outside); string outsideFile = Path.Combine(outside, "keep.txt"); File.WriteAllText(outsideFile, "keep");
            string junction = Path.Combine(folder, "linked-folder");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J " + AutoStart.Quote(junction) + " " + AutoStart.Quote(outside)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            { process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("无法创建隔离测试的目录链接。"); }
            invalid = false; try { MediaDeletion.Plan(new[] { folderEntry }, MediaDeleteKind.Folder, root); } catch (InvalidOperationException) { invalid = true; }
            SelfTests.Assert(invalid && File.Exists(outsideFile) && File.Exists(video), "删除文件夹拒绝内部目录链接，保留链接外部数据", report);
            Directory.Delete(junction);
            plan = MediaDeletion.Plan(new[] { folderEntry, local }, MediaDeleteKind.Folder, root);
            MediaDeletion.Execute(plan, CancellationToken.None);
            SelfTests.Assert(plan.Targets.Length == 1 && !Directory.Exists(folder) && Directory.Exists(root), "删除整个文件夹包含视频、弹幕和其他内容，重复父目录去重", report);
        }
    }
}
