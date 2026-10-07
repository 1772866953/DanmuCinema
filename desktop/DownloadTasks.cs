using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema.Desktop
{
    public sealed class DownloadTask
    {
        public Dictionary<string, object> Item, Remote;
        public string Status, Origin, Detail;
        public DateTime UpdatedUtc;
        public string Path { get { return Json.Text(Item, "Path"); } }
        public string Name { get { return MediaPresentation.Title(Item); } }
        public string Filename { get { return System.IO.Path.GetFileName(Path); } }
        public bool Retryable { get { return Status.Contains("失败") || Status.Contains("超时") || Status == "待确认" || Status == "已暂停" || Status == "已停止"; } }
    }
    public sealed partial class DesktopController
    {
        public readonly List<DownloadTask> DownloadTasks = new List<DownloadTask>();
        List<Dictionary<string, object>> preparationPlan;
        CancellationTokenSource preparationCancellation;
        int preparationIndex;
        public bool Preparing { get; private set; }
        public bool PreparationPaused { get { return !Preparing && preparationPlan != null && preparationIndex < preparationPlan.Count; } }
        public string PreparationStatus { get; private set; }
        internal DownloadTask FindTask(Dictionary<string, object> item)
        { return DownloadTasks.FirstOrDefault(x => String.Equals(x.Path, Json.Text(item, "Path"), StringComparison.OrdinalIgnoreCase)); }
        internal void SetDownloadState(Dictionary<string, object> item, string status, string origin, string detail, Dictionary<string, object> remote = null)
        {
            if (String.IsNullOrEmpty(Json.Text(item, "Path"))) return;
            var task = FindTask(item);
            if (task == null) { task = new DownloadTask { Item = item }; DownloadTasks.Add(task); if (DownloadTasks.Count > 500) DownloadTasks.RemoveAt(0); }
            task.Status = status ?? "待下载"; task.Origin = origin; task.Detail = detail; task.UpdatedUtc = DateTime.UtcNow;
            if (origin == "提前准备" || origin == "播放准备") task.Remote = null;
            if (remote != null) task.Remote = remote;
        }
        void AutoProgress(Dictionary<string, object> item, string status, string detail, string origin)
        {
            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed || Closing) return;
                SetDownloadState(item, status, origin, detail); Publish();
            }));
        }
        public async Task DownloadSingle(Dictionary<string, object> item, Dictionary<string, object> episode)
        {
            string video = Json.Text(item, "Path");
            if (!File.Exists(video)) throw new InvalidOperationException("本地视频文件不存在。");
            if (CancellingDownloads) return;
            var operation = BeginDownload(new[] { item });
            SetDownloadState(item, "下载中", "单集下载", Gateway.Catalog.EpisodeSource(episode), episode); Publish();
            try
            {
                string content = await Gateway.Catalog.Download(episode, operation.Cancellation.Token);
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                int count = DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count;
                if (count == 0) throw new InvalidOperationException("这集没有可用弹幕，已有文件未更改。");
                SettingsStore.AtomicWrite(Path.ChangeExtension(video, ".xml"), content, false); Gateway.Catalog.RecordAssociation(item, episode);
                SetDownloadState(item, "已保存 " + count + " 条", "单集下载", Gateway.Catalog.EpisodeSource(episode), episode);
                await RefreshMetadata();
            }
            catch (Exception error) { SetDownloadState(item, "下载失败", "单集下载", error.Message, episode); throw; }
            finally { FinishDownload(operation); }
        }
        public async Task PrepareVideos(IEnumerable<Dictionary<string, object>> videos, bool resume = false,
            Func<Dictionary<string, object>, CancellationToken, Task<bool>> prepare = null)
        {
            if (Busy || Loading || BatchRunning || Preparing || Closing || CancellingDownloads) return;
            if (!resume)
            {
                preparationPlan = videos.Where(x => File.Exists(Json.Text(x, "Path")) && !HasSidecar(x)).GroupBy(x => Json.Text(x, "Path"), StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList(); preparationIndex = 0;
                foreach (var item in preparationPlan) SetDownloadState(item, "等待准备", "提前准备", "缓存优先，仅补齐缺少的弹幕");
            }
            if (preparationPlan == null || preparationIndex >= preparationPlan.Count) { PreparationStatus = "没有需要准备的影片，已有弹幕会保留。"; Publish(); return; }
            var operation = BeginDownload(preparationPlan);
            Preparing = BatchRunning = true; preparationCancellation = operation.Cancellation; var token = preparationCancellation.Token;
            try
            {
                while (preparationIndex < preparationPlan.Count)
                {
                    token.ThrowIfCancellationRequested(); var item = preparationPlan[preparationIndex];
                    PreparationStatus = "准备 " + (preparationIndex + 1) + " / " + preparationPlan.Count + " · " + MediaPresentation.Title(item);
                    SetDownloadState(item, "匹配中", "提前准备", "文件指纹 → 文件名"); Publish();
                    try
                    {
                        bool saved = prepare == null ? await Automatic.PrepareQueuedItem(item, token) : await prepare(item, token);
                        token.ThrowIfCancellationRequested();
                        var current = FindTask(item);
                        if (saved || HasSidecar(item)) SetDownloadState(item, "已就绪", "提前准备", current == null ? "" : current.Detail);
                        else if (current == null || current.Status == "匹配中") SetDownloadState(item, "待确认", "提前准备", "没有唯一可靠候选，请手动匹配");
                    }
                    catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); SetDownloadState(item, "准备超时", "提前准备", "可手动重试"); }
                    catch (Exception error) { SetDownloadState(item, "准备失败", "提前准备", error.Message); }
                    preparationIndex++; Publish();
                    if (preparationIndex < preparationPlan.Count) await Task.Delay(Settings.DownloadIntervalSeconds * 1000, token);
                }
                PreparationStatus = "提前准备完成。无法可靠识别的影片留待手动确认。";
            }
            catch (OperationCanceledException)
            {
                PreparationStatus = "已暂停，完成的弹幕已保留。";
                foreach (var item in preparationPlan.Skip(preparationIndex)) SetDownloadState(item, "已暂停", "提前准备", "可继续未完成任务");
            }
            finally { Preparing = BatchRunning = false; preparationCancellation = null; Publish(); }
            try { await RefreshMetadata(); } finally { FinishDownload(operation); }
        }
        internal static bool HasSidecar(Dictionary<string, object> item)
        { try { var file = new FileInfo(Path.ChangeExtension(Json.Text(item, "Path"), ".xml")); return file.Exists && file.Length > 0; } catch { return false; } }
        public void PausePreparation() { if (preparationCancellation != null) preparationCancellation.Cancel(); }
        public async void StartPreparation(IEnumerable<Dictionary<string, object>> videos, bool resume = false)
        { try { await PrepareVideos(videos, resume); } catch (Exception error) { PreparationStatus = error.Message; Publish(); } }
        public async void RetryTask(DownloadTask task)
        { if (task.Remote == null) { StartPreparation(new[] { task.Item }); return; } await Execute(() => DownloadSingle(task.Item, task.Remote), false); }
        public void RetryBatch(bool failuresOnly)
        {
            if (BatchPlan == null || BatchRunning || Busy || Loading) return;
            foreach (var item in BatchPlan) item.Selected = item.Remote != null && (failuresOnly ? item.Status.Contains("失败") || item.Status.Contains("超时") : !item.Status.StartsWith("已保存") && item.Status != "保留已有 XML");
            Publish(); StartBatch();
        }
    }
}
