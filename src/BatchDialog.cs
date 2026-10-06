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
    public sealed class BatchDialog : Form
    {
        readonly List<BatchEntry> plan;
        readonly DanmuCatalog catalog;
        readonly DataGridView grid;
        readonly Label status;
        readonly CheckBox keep;
        readonly Button start, stop;
        CancellationTokenSource cancellation;
        bool busy;
        public BatchDialog(DanmuCatalog catalog, List<BatchEntry> plan, string title, bool keepExisting = true)
        {
            this.plan = plan; this.catalog = catalog;
            Text = "全部下载 · " + title; ClientSize = new Size(980, 580); MinimumSize = new Size(800, 500);
            Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterParent;
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.Controls.Add(new WrappedLabel { Text = title + "\r\n请核对下表。按集号关联本地本季文件；无法判断或重复的集数会跳过。" }, 0, 0);
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "下载", FillWeight = 12 });
            grid.Columns.Add("Number", "集号"); grid.Columns[1].FillWeight = 12;
            grid.Columns.Add("Local", "本地文件"); grid.Columns[2].FillWeight = 90;
            grid.Columns.Add("Remote", "在线集数"); grid.Columns[3].FillWeight = 60;
            grid.Columns.Add("Status", "状态"); grid.Columns[4].FillWeight = 55;
            foreach (DataGridViewColumn column in grid.Columns) if (column.Index != 0) column.ReadOnly = true;
            foreach (var entry in plan)
            {
                int row = grid.Rows.Add(entry.Selected, entry.Number == 0 ? "?" : entry.Number.ToString(), Path.GetFileName(Json.Text(entry.Local, "Path")), entry.Remote == null ? "—" : Json.Text(entry.Remote, "Number") + " " + Json.Text(entry.Remote, "Title"), entry.Status);
                grid.Rows[row].Tag = entry;
                if (!entry.Selected) { grid.Rows[row].Cells[0].ReadOnly = true; grid.Rows[row].DefaultCellStyle.ForeColor = Color.Gray; }
            }
            outer.Controls.Add(grid, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 5, 0, 0) };
            keep = new CheckBox { Text = "保留已有 XML（取消勾选将直接覆盖）", Checked = keepExisting, AutoSize = true, Margin = new Padding(0, 6, 20, 0) };
            start = new Button { Text = "开始全部下载", AutoSize = true }; stop = new Button { Text = "停止下载", AutoSize = true, Enabled = false };
            start.Click += async (s, e) => await Download(); stop.Click += (s, e) => cancellation.Cancel();
            actions.Controls.Add(keep); actions.Controls.Add(start); actions.Controls.Add(stop); outer.Controls.Add(actions, 0, 2);
            status = new WrappedLabel { Text = "可匹配 " + plan.Count(x => x.Selected) + " 集，共 " + plan.Count + " 个本地文件。" }; outer.Controls.Add(status, 0, 3); Controls.Add(outer);
            FormClosing += (s, e) => { if (busy) { cancellation.Cancel(); e.Cancel = true; status.Text = "正在停止；已完成的文件保留。"; } };
        }
        async Task Download()
        {
            if (busy) return;
            grid.EndEdit(); foreach (DataGridViewRow row in grid.Rows) ((BatchEntry)row.Tag).Selected = Convert.ToBoolean(row.Cells[0].Value);
            if (!plan.Any(x => x.Selected)) { status.Text = "没有选择可下载的集数。"; return; }
            busy = true; grid.Enabled = keep.Enabled = start.Enabled = false; stop.Enabled = true;
            using (cancellation = new CancellationTokenSource())
            {
                try
                {
                    var result = await BatchDownloads.Run(plan, episode => catalog.Download(episode, cancellation.Token), keep.Checked, cancellation.Token, (entry, done, total) =>
                    {
                        foreach (DataGridViewRow row in grid.Rows) if (Object.ReferenceEquals(row.Tag, entry)) row.Cells[4].Value = entry.Status;
                        status.Text = "处理 " + done + " / " + total + " · 第 " + entry.Number + " 集 " + entry.Status;
                    }, 1000, entry => catalog.RecordAssociation(entry.Local, entry.Remote));
                    foreach (DataGridViewRow row in grid.Rows) row.Cells[4].Value = ((BatchEntry)row.Tag).Status;
                    status.Text = (result.Cancelled ? "已停止" : "下载完成") + "：保存 " + result.Saved + " 集，跳过 " + result.Skipped + " 集，失败 " + result.Failed + " 集。";
                    Log.Write(status.Text);
                }
                catch { status.Text = "批量下载未完成，已保存的文件保留。"; }
                finally { busy = false; grid.Enabled = keep.Enabled = start.Enabled = true; stop.Enabled = false; }
            }
        }
    }
}
