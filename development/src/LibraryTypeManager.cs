using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed class LibraryTypeManager
    {
        readonly JellyfinApi api;
        readonly string journal;
        readonly Func<Task> settle;
        static readonly SemaphoreSlim changing = new SemaphoreSlim(1, 1);
        public LibraryTypeManager(JellyfinApi api, Func<Task> settle = null)
        { this.api = api; journal = Path.Combine(Paths.Data, "media-library-change.json"); this.settle = settle ?? (() => new LibraryScanner(api).Load(true, null, CancellationToken.None)); }
        public async Task<Dictionary<string, object>[]> Libraries()
        { return Json.Read<object[]>(await api.Request("GET", "Library/VirtualFolders", null, true)).OfType<Dictionary<string, object>>().ToArray(); }
        static string Type(Dictionary<string, object> library) { string type = Json.Text(library, "CollectionType"); return type == "" ? "mixed" : type; }
        static bool PathsEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            Func<Dictionary<string, object>, string[]> paths = x => Json.Array(x, "Locations").Select(p => Path.GetFullPath(Convert.ToString(p)).TrimEnd('\\')).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return paths(a).SequenceEqual(paths(b), StringComparer.OrdinalIgnoreCase);
        }
        async Task Create(Dictionary<string, object> original, string type)
        {
            await api.Request("POST", "Library/VirtualFolders?name=" + Uri.EscapeDataString(Json.Text(original, "Name")) + (type == "mixed" ? "" : "&collectionType=" + Uri.EscapeDataString(type)) + "&refreshLibrary=false", new { LibraryOptions = Json.Child(original, "LibraryOptions") }, true);
        }
        void ClearJournal() { if (File.Exists(journal)) File.Delete(journal); }
        async Task<bool> Recover(Dictionary<string, object> original, string target)
        {
            var library = (await Libraries()).FirstOrDefault(x => Json.Text(x, "Name") == Json.Text(original, "Name"));
            if (library != null)
            {
                if (!PathsEqual(library, original)) throw new InvalidOperationException("媒体库目录已被其他操作修改，已保留恢复配置，请先检查服务器媒体库设置。");
                if (Type(library) == target) { ClearJournal(); return true; }
                if (Type(library) == Type(original)) { ClearJournal(); return false; }
                throw new InvalidOperationException("媒体库类型已被其他操作修改，已保留恢复配置，请先检查服务器媒体库设置。");
            }
            await Create(original, Type(original));
            var restored = (await Libraries()).FirstOrDefault(x => Json.Text(x, "Name") == Json.Text(original, "Name"));
            if (restored == null || Type(restored) != Type(original) || !PathsEqual(restored, original)) throw new InvalidOperationException("未能恢复媒体库配置，请检查服务器日志。");
            await settle();
            ClearJournal(); return false;
        }
        public async Task RecoverPending()
        {
            await changing.WaitAsync();
            try
            {
                if (!File.Exists(journal)) return;
                var record = Json.Object(File.ReadAllText(journal));
                await Recover(Json.Child(record, "Original"), Json.Text(record, "Target"));
            }
            finally { changing.Release(); }
        }
        public async Task<bool> Change(Dictionary<string, object> selected, string target)
        {
            if (!MediaAuto.Valid(target)) throw new ArgumentException("媒体库识别方式不正确。");
            if (selected == null) throw new InvalidOperationException("请选择一个已添加的媒体库。");
            await changing.WaitAsync();
            try
            {
                if (File.Exists(journal)) throw new InvalidOperationException("上次媒体库调整尚未完成，请重新打开调整窗口恢复配置。");
                string id = Json.Text(selected, "ItemId"), name = Json.Text(selected, "Name");
                var candidates = (await Libraries()).Where(x => id != "" ? Json.Text(x, "ItemId") == id : Json.Text(x, "Name") == name).ToArray();
                if (candidates.Length != 1 || Json.Text(candidates[0], "Name") != name) throw new InvalidOperationException("媒体库已经改变，请刷新列表后重新选择。");
                var original = candidates[0];
                if (!MediaAuto.Valid(Type(original))) throw new InvalidOperationException("此媒体库不属于视频类型，不能调整。");
                if (Type(original) == target) return false;
                if (Json.Child(original, "LibraryOptions") == null || Json.Array(original, "Locations").Length == 0) throw new InvalidOperationException("无法读取完整媒体库配置，已停止调整。");
                foreach (var path in Json.Array(original, "Locations")) if (!Directory.Exists(Convert.ToString(path))) throw new DirectoryNotFoundException("媒体目录当前不可访问，请先恢复目录后再调整。");
                var tasks = Json.Read<object[]>(await api.Request("GET", "ScheduledTasks", null, true)).OfType<Dictionary<string, object>>();
                if (tasks.Any(x => Json.Text(x, "Key") == "RefreshLibrary" && (Json.Text(x, "State") == "Running" || Json.Text(x, "State") == "Cancelling"))) throw new InvalidOperationException("媒体库正在扫描，请等待扫描完成后再调整。");
                if ((await api.Sessions()).OfType<Dictionary<string, object>>().Any(x => Json.Child(x, "NowPlayingItem") != null)) throw new InvalidOperationException("目前有客户端正在播放，请结束播放后再调整媒体库。");
                SettingsStore.AtomicWrite(journal, Json.Write(new { Original = original, Target = target }));
                Exception failure = null;
                try
                {
                    // Virtual folder removal only touches the server's library
                    // definition. Never use Items/{id}/DELETE or media deletion.
                    await api.Request("DELETE", "Library/VirtualFolders?name=" + Uri.EscapeDataString(name) + "&refreshLibrary=true", null, true);
                    // Let the server remove the old category index while the
                    // virtual library is absent; otherwise existing Movie rows
                    // survive even after a mixed/TV library is re-created.
                    await settle();
                    await Create(original, target);
                    var updated = (await Libraries()).FirstOrDefault(x => Json.Text(x, "Name") == name);
                    if (updated == null || Type(updated) != target || !PathsEqual(updated, original)) throw new InvalidOperationException("服务器未确认媒体库识别方式已调整。");
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    bool completed;
                    try { completed = await Recover(original, target); }
                    catch (Exception) { throw new InvalidOperationException("调整未完成，自动恢复暂时失败。原配置已保存在数据目录；恢复服务器连接后重新打开调整窗口重试。"); }
                    if (!completed) throw new InvalidOperationException("调整失败，已恢复原媒体库配置。" + failure.Message);
                }
                ClearJournal(); Log.Write("媒体库「" + name + "」识别方式已调整为" + MediaAuto.Label(target) + "。"); return true;
            }
            finally { changing.Release(); }
        }
    }
}
