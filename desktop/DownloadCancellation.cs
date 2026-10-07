using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema.Desktop
{
    internal sealed class DownloadOperation
    {
        public Dictionary<string, object>[] Items;
        public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
        public bool Cleared;
    }
    public sealed partial class DesktopController
    {
        DownloadOperation currentDownload;
        public bool CancellingDownloads { get; private set; }
        public bool CanCancelDownloads { get { return !CancellingDownloads && currentDownload != null && !currentDownload.Cleared && currentDownload.Items.Length > 0; } }
        DownloadOperation BeginDownload(IEnumerable<Dictionary<string, object>> items)
        {
            currentDownload = new DownloadOperation { Items = items.GroupBy(x => Json.Text(x, "Path"), StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray() };
            return currentDownload;
        }
        void FinishDownload(DownloadOperation operation)
        { operation.Cancellation.Dispose(); operation.Completion.TrySetResult(true); Publish(); }
        public async Task CancelCurrentDownloads()
        {
            if (!CanCancelDownloads) return;
            var operation = currentDownload; CancellingDownloads = true; bool resumeAutomatic = Automatic.Running;
            Status = "正在取消本次任务，等待文件写入结束…"; Publish();
            try
            {
                if (!operation.Completion.Task.IsCompleted) operation.Cancellation.Cancel();
                await operation.Completion.Task;
                // Stop only the automatic danmaku worker while its sidecars are removed.
                await Automatic.Stop();
                var items = operation.Items;
                var entries = items.Select(x => LibraryEntry.Read(x, new Dictionary<string, DanmuAssociation>())).ToArray();
                var plan = await Task.Run(() => items.Any(x => File.Exists(Path.ChangeExtension(Json.Text(x, "Path"), ".xml")))
                    ? MediaDeletion.Plan(entries, MediaDeleteKind.Danmu, Settings.MediaFolder)
                    : new MediaDeletePlan { Root = MediaDeletion.FullPath(Settings.MediaFolder), Kind = MediaDeleteKind.Danmu, Targets = new MediaDeleteTarget[0] });
                await Task.Run(() => MediaDeletion.Execute(plan, CancellationToken.None));
                Automatic.SuppressUntilPlaybackEnds(items.Select(x => Json.Text(x, "Id")));
                foreach (var item in items) SetDownloadState(item, "已取消并清理", "取消下载", "已清理本次任务的同名 XML");
                if (preparationPlan != null && preparationPlan.Any(x => items.Any(y => Json.Text(x, "Path") == Json.Text(y, "Path")))) { preparationPlan = null; preparationIndex = 0; }
                if (BatchPlan != null) foreach (var row in BatchPlan.Where(x => items.Any(y => Json.Text(x.Local, "Path") == Json.Text(y, "Path")))) { row.Selected = false; row.Status = "已取消并清理"; }
                operation.Cleared = true;
                Status = BatchStatus = PreparationStatus = "已取消本次任务，清理 " + plan.Targets.Length + " 个弹幕文件。"; Log.Write(Status);
                await RefreshMetadata();
            }
            catch (Exception error) { Status = "任务已停止，弹幕清理未完成：" + error.Message; Log.Write(Status); }
            finally { CancellingDownloads = false; if (resumeAutomatic && !Closing && !disposed) Automatic.Start(); Publish(); }
        }
    }
}
