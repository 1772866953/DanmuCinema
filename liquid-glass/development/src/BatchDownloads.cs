using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed class BatchResult { public int Saved, Skipped, Failed; public bool Cancelled; }
    public static class BatchDownloads
    {
        public static async Task<BatchResult> Run(List<BatchEntry> entries, Func<Dictionary<string, object>, Task<string>> download, bool keepExisting, CancellationToken cancellation, Action<BatchEntry, int, int> progress, int delayMs = 1000, Action<BatchEntry> onSaved = null)
        {
            var result = new BatchResult(); int done = 0;
            foreach (var entry in entries)
            {
                if (cancellation.IsCancellationRequested) { result.Cancelled = true; break; }
                if (!entry.Selected || entry.Remote == null) { result.Skipped++; done++; continue; }
                string video = Json.Text(entry.Local, "Path"), xmlPath = Path.ChangeExtension(video, ".xml");
                if (!File.Exists(video)) { entry.Status = "本地文件不存在"; result.Failed++; }
                else if (keepExisting && File.Exists(xmlPath)) { entry.Status = "保留已有 XML"; result.Skipped++; }
                else
                {
                    entry.Status = "下载中"; if (progress != null) progress(entry, done, entries.Count);
                    try
                    {
                        string content = await download(entry.Remote);
                        cancellation.ThrowIfCancellationRequested();
                        int count = DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count;
                        if (count == 0) { entry.Status = "没有弹幕"; result.Skipped++; }
                        else
                        {
                            SettingsStore.AtomicWrite(xmlPath, content, false); entry.Status = "已保存 " + count + " 条"; result.Saved++;
                            if (onSaved != null) try { onSaved(entry); } catch { Log.Write("弹幕已保存，附加来源记录失败。"); }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellation.IsCancellationRequested) { entry.Status = "已停止"; result.Cancelled = true; break; }
                        entry.Status = "下载超时，可单独重试"; result.Failed++;
                    }
                    catch { entry.Status = "下载失败，可单独重试"; result.Failed++; }
                    if (delayMs > 0 && !cancellation.IsCancellationRequested)
                    {
                        try { await Task.Delay(delayMs, cancellation); } catch (OperationCanceledException) { result.Cancelled = true; }
                    }
                }
                done++; if (progress != null) progress(entry, done, entries.Count);
            }
            return result;
        }
    }
}