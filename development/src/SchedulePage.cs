using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed partial class MainForm
    {
        readonly Scheduler scheduler;
        readonly System.Windows.Forms.Timer scheduleTimer;
        ToolStripItem traySchedule, trayCancelSchedule;
        ComboBox scheduleMode, scheduleAction;
        NumericUpDown scheduleHours, scheduleMinutes, scheduleSeconds;
        DateTimePicker scheduleDate, scheduleTime;
        CheckBox scheduleAwake;
        CheckBox[] scheduleDays;
        Control scheduleEditor, delayInputs, presetInputs, dateInputs;
        Button scheduleStart, scheduleCancel;
        Label scheduleCountdown, scheduleStatus, scheduleTarget, scheduleDescription;
        ProgressBar scheduleProgress;
        bool scheduleWarningShown, scheduleAwakeHeld;
        double scheduleInitialSeconds;

        void BuildSchedule()
        {
            var page = Page("schedule"); page.AutoScroll = false;
            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var stack = Stack(content);
            var state = Card("当前任务", 170);
            var stateInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            scheduleCountdown = new Label { Text = "00:00:00", Dock = DockStyle.Fill, Height = 40, Font = new Font("Consolas", 24, FontStyle.Bold), ForeColor = accent };
            scheduleStatus = TextLabel("尚未设置定时任务", 25);
            scheduleTarget = TextLabel("关闭窗口放入托盘后，仍会继续计时。", 25);
            scheduleProgress = new ProgressBar { Dock = DockStyle.Fill, Height = 6, Maximum = 1000, Margin = new Padding(0, 5, 0, 0) };
            stateInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            stateInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            stateInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            stateInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            stateInner.Controls.Add(scheduleCountdown); stateInner.Controls.Add(scheduleStatus); stateInner.Controls.Add(scheduleTarget); stateInner.Controls.Add(scheduleProgress);
            state.Controls.Add(stateInner); stateInner.BringToFront(); Add(stack, state);

            var editor = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Color.White, Padding = new Padding(18, 12, 18, 8) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var inputs = editor; scheduleEditor = editor;
            scheduleMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            scheduleMode.Items.AddRange(new object[] { "倒计时", "指定时间", "每周计划" }); scheduleMode.SelectedIndex = 0;
            Add(inputs, Actions(ScheduleLabel("计时方式"), scheduleMode));
            scheduleHours = ScheduleNumber(720, 0); scheduleMinutes = ScheduleNumber(59, 30); scheduleSeconds = ScheduleNumber(59, 0);
            delayInputs = Actions(scheduleHours, ScheduleLabel("小时", 45), scheduleMinutes, ScheduleLabel("分钟", 45), scheduleSeconds, ScheduleLabel("秒", 32));
            Add(inputs, delayInputs);
            var presets = Actions();
            foreach (int minutes in new[] { 15, 30, 60, 120 })
            {
                int value = minutes;
                var preset = ScheduleButton(minutes < 60 ? minutes + " 分钟" : minutes / 60 + " 小时", false);
                preset.MinimumSize = new Size(80, 32);
                preset.Click += (s, e) => { scheduleHours.Value = value / 60; scheduleMinutes.Value = value % 60; scheduleSeconds.Value = 0; };
                presets.Controls.Add(preset);
            }
            Add(inputs, presets); presetInputs = presets;
            scheduleDate = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 160, Value = DateTime.Now.AddMinutes(30).Date };
            scheduleTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Width = 140, Value = DateTime.Now.AddMinutes(30) };
            dateInputs = Actions(ScheduleLabel("执行日期"), scheduleDate, ScheduleLabel("时间", 45), scheduleTime);
            Add(inputs, dateInputs); dateInputs.Visible = false;
            scheduleDays = new CheckBox[7]; var weekInputs = Actions(); weekInputs.Visible = false;
            for (int day = 0; day < 7; day++) { scheduleDays[day] = new CheckBox { Text = WeeklyPlan.DayNames[day], Checked = true, AutoSize = true }; weekInputs.Controls.Add(scheduleDays[day]); } Add(inputs, weekInputs);
            scheduleMode.SelectedIndexChanged += (s, e) => { delayInputs.Visible = presetInputs.Visible = scheduleMode.SelectedIndex == 0; dateInputs.Visible = scheduleMode.SelectedIndex > 0; scheduleDate.Enabled = scheduleMode.SelectedIndex == 1; weekInputs.Visible = scheduleMode.SelectedIndex == 2; };
            scheduleAction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            foreach (PowerAction action in Enum.GetValues(typeof(PowerAction))) scheduleAction.Items.Add(PowerActions.Name(action));
            scheduleAction.SelectedIndex = (int)PowerAction.Hibernate;
            Add(inputs, Actions(ScheduleLabel("到时操作"), scheduleAction));
            scheduleDescription = TextLabel(PowerActions.Description(PowerAction.Hibernate), 42); Add(inputs, scheduleDescription);
            scheduleAction.SelectedIndexChanged += (s, e) => scheduleDescription.Text = PowerActions.Description((PowerAction)scheduleAction.SelectedIndex);
            scheduleAwake = new CheckBox { Text = "定时期间阻止电脑自动睡眠（屏幕仍可熄灭）", AutoSize = true };
            Add(inputs, Actions(scheduleAwake)); Add(stack, editor);
            Add(stack, TextLabel("执行前 15 秒提醒，可点击取消或按 Esc；任务开始执行后无法撤回。\r\n倒计时最长 30 天；退出程序会取消任务，再次启动不会恢复。", 58));
            scheduleStart = ScheduleButton("开始定时", true);
            scheduleCancel = ScheduleButton("取消定时", false); scheduleCancel.Enabled = false;
            scheduleStart.Click += (s, e) => StartSchedule();
            scheduleCancel.Click += (s, e) => CancelSchedule();
            var commands = Actions(scheduleStart, scheduleCancel); commands.Dock = DockStyle.Bottom; commands.Padding = new Padding(0, 8, 0, 0);
            page.Controls.Add(content); page.Controls.Add(commands);
        }
        Label ScheduleLabel(string text, int width = 90) { return new Label { Text = text, Width = width, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted }; }
        NumericUpDown ScheduleNumber(int maximum, int value) { return new NumericUpDown { Maximum = maximum, Value = value, Width = 78, Margin = new Padding(0, 3, 6, 0) }; }
        Button ScheduleButton(string text, bool primary)
        {
            // Scheduling controls stay usable independently of server and download operations.
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(112, 38), FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.FromArgb(238, 243, 247), ForeColor = primary ? Color.White : ink, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 10, 0), Cursor = Cursors.Hand, FlatAppearance = { BorderSize = 0 } };
        }
        void StartSchedule()
        {
            if (scheduler.Active || scheduler.State == ScheduleState.Executing || closing) return;
            try
            {
                var action = (PowerAction)scheduleAction.SelectedIndex;
                PowerActions.Validate(action);
                if (scheduleMode.SelectedIndex == 0)
                    scheduler.StartDelay(TimeSpan.FromHours((double)scheduleHours.Value) + TimeSpan.FromMinutes((double)scheduleMinutes.Value) + TimeSpan.FromSeconds((double)scheduleSeconds.Value), action);
                else if (scheduleMode.SelectedIndex == 2)
                {
                    int mask = 0; for (int day = 0; day < 7; day++) if (scheduleDays[day].Checked) mask |= 1 << day;
                    scheduler.StartWeekly(mask, scheduleTime.Value.TimeOfDay, action);
                }
                else
                {
                    var local = DateTime.SpecifyKind(scheduleDate.Value.Date + scheduleTime.Value.TimeOfDay, DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) throw new ArgumentException("此时间处于夏令时切换区间，请使用倒计时。");
                    scheduler.StartAt(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)), action);
                }
                if (scheduleAwake.Checked) { PowerActions.KeepAwake(true); scheduleAwakeHeld = true; }
                scheduleWarningShown = false; scheduleInitialSeconds = scheduler.Remaining.TotalSeconds;
                scheduleTarget.Text = "预计执行 " + scheduler.Target.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                Log.Write("定时" + PowerActions.Name(action) + "已开始，" + scheduleTarget.Text);
                RenderSchedule();
            }
            catch (Exception error)
            {
                scheduler.Cancel(); ReleaseScheduleAwake(); SetScheduleControls();
                scheduleStatus.Text = "无法开始：" + error.Message;
                MessageBox.Show(this, error.Message, "无法开始定时", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        void CancelSchedule()
        {
            if (!scheduler.Cancel()) return;
            ReleaseScheduleAwake(); SetScheduleControls();
            scheduleCountdown.Text = "00:00:00"; scheduleProgress.Value = 0;
            scheduleStatus.Text = "已取消定时，原定操作不会执行。";
            scheduleTarget.Text = "可以重新设置定时任务。";
            traySchedule.Text = "当前没有定时任务";
            Log.Write("已取消定时" + PowerActions.Name(scheduler.Action));
        }
        void SetScheduleControls()
        {
            bool locked = scheduler.Active || scheduler.State == ScheduleState.Executing;
            scheduleEditor.Enabled = !locked; scheduleStart.Enabled = !locked;
            scheduleCancel.Enabled = trayCancelSchedule.Enabled = scheduler.Active;
        }
        void RenderSchedule()
        {
            SetScheduleControls();
            scheduleCountdown.Text = Scheduler.FormatRemaining(scheduler.Remaining);
            scheduleTarget.Text = "预计执行 " + scheduler.Target.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            scheduleProgress.Value = (int)Math.Max(0, Math.Min(1000, (1 - scheduler.Remaining.TotalSeconds / Math.Max(1, scheduleInitialSeconds)) * 1000));
            string action = PowerActions.Name(scheduler.Action);
            traySchedule.Text = action + " · 剩余 " + scheduleCountdown.Text;
            scheduleStatus.Text = action + (scheduler.State == ScheduleState.Warning ? " · 即将执行，仍可取消" : " · 定时进行中");
            if (busy && scheduler.Remaining <= TimeSpan.FromSeconds(15)) scheduleStatus.Text = action + " · 等待当前操作完成后提醒，仍可取消";
            if (scheduler.State == ScheduleState.Warning && !scheduleWarningShown && !busy)
            {
                scheduleWarningShown = true;
                RestoreWindow(); Navigate("schedule"); scheduleCancel.Focus();
                Notify("即将" + action, "可点击取消定时或按 Esc。执行阶段开始后无法撤回。");
            }
        }
        async Task TickSchedule()
        {
            if (!scheduler.Active || closing) return;
            // Avoid stopping a service while it is being configured or downloading comments.
            // Poll's stall handling gives a fresh cancellation window after a long operation.
            if (busy || libraryLoading) { RenderSchedule(); return; }
            if (!scheduleWarningShown && scheduler.State == ScheduleState.Warning && scheduler.Remaining <= TimeSpan.Zero) scheduler.OnResume();
            if (!scheduler.Poll()) { RenderSchedule(); return; }
            SetScheduleControls(); ReleaseScheduleAwake();
            scheduleCountdown.Text = "00:00:00"; scheduleProgress.Value = 1000;
            scheduleStatus.Text = "正在执行" + PowerActions.Name(scheduler.Action) + "，无法再取消。";
            busy = true;
            try
            {
                if (scheduler.Action == PowerAction.StopServices) await StopAll();
                else await Task.Run(() => PowerActions.Execute(scheduler.Action));
                scheduler.Finish(true);
                scheduleStatus.Text = scheduler.Action == PowerAction.StopServices ? "视频与弹幕服务已停止。" : "已向 Windows 提交" + PowerActions.Name(scheduler.Action) + "请求。";
                Log.Write(scheduleStatus.Text);
            }
            catch (Exception error)
            {
                scheduler.Finish(false); scheduleStatus.Text = "执行失败：" + error.Message;
                Log.Write(scheduleStatus.Text); RestoreWindow(); Navigate("schedule");
                MessageBox.Show(this, error.Message, "定时操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false; SetScheduleControls();
                if (scheduler.Active)
                {
                    try { if (scheduleAwake.Checked) { PowerActions.KeepAwake(true); scheduleAwakeHeld = true; } scheduleWarningShown = false; scheduleInitialSeconds = scheduler.Remaining.TotalSeconds; RenderSchedule(); }
                    catch (Exception error) { scheduler.Cancel(); ReleaseScheduleAwake(); SetScheduleControls(); scheduleStatus.Text = "计划已停止：" + error.Message; Log.Write(scheduleStatus.Text); }
                }
                else { traySchedule.Text = "当前没有定时任务"; scheduleTarget.Text = "本次任务已结束，可以重新设置。"; }
            }
            await UpdateStatus();
        }
        void ReleaseScheduleAwake()
        {
            if (!scheduleAwakeHeld) return;
            try { PowerActions.KeepAwake(false); }
            catch (Exception error) { Log.Write("恢复自动睡眠设置失败：" + error.Message); }
            finally { scheduleAwakeHeld = false; }
        }
        protected override void WndProc(ref Message message)
        {
            if (libraryMouseNavigation != null && libraryMouseNavigation.PreFilterMessage(ref message)) return;
            if (message.Msg == 0x218 && (message.WParam.ToInt32() == 0x12 || message.WParam.ToInt32() == 0x7) && scheduler != null)
            { scheduler.OnResume(); scheduleWarningShown = false; }
            base.WndProc(ref message);
        }
    }
}
