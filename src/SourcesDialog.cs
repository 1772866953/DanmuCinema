using System;
using System.Drawing;
using System.Windows.Forms;

namespace DanMuLAN
{
    public sealed class SourcesDialog : Form
    {
        public SourcesDialog(AppSettings settings)
        {
            Text = "弹幕来源 · 同时搜索"; Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(850, 630); MinimumSize = new Size(780, 650); StartPosition = FormStartPosition.CenterParent;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var heading = new Label { Text = "勾选的来源会同时查询；单个来源失败不会影响其他结果。", AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
            panel.Controls.Add(heading);
            var animeko = Check("Animeko 公益弹幕（Bangumi 动漫目录）", settings.EnableAnimeko);
            var bahamut = Check("巴哈姆特动画疯（支持繁简体名称搜索）", settings.EnableBahamut);
            var existing = Check("保留现有平台来源（B 站、爱奇艺、优酷等）", settings.EnableExistingDanmu);
            var only = Check("iPad 搜索默认只看动漫；电脑搜索窗口可切换显示全部", settings.AnimeOnly);
            var dandan = Check("弹弹play 官方 API（需自己的 AppId 和 AppSecret）", settings.EnableDandan);
            panel.Controls.Add(animeko); panel.Controls.Add(bahamut); panel.Controls.Add(existing); panel.Controls.Add(only); panel.Controls.Add(dandan);
            var credentials = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 12) };
            var appId = new TextBox { Width = 160, Text = settings.DandanAppId ?? "" };
            var secret = new TextBox { Width = 340, UseSystemPasswordChar = true, Text = SettingsStore.Unprotect(settings.EncryptedDandanSecret) };
            credentials.Controls.Add(new Label { Text = "AppId", AutoSize = true, Margin = new Padding(0, 5, 10, 0) }); credentials.Controls.Add(appId);
            credentials.Controls.Add(new Label { Text = "AppSecret", AutoSize = true, Margin = new Padding(15, 5, 10, 0) }); credentials.Controls.Add(secret);
            panel.Controls.Add(credentials);
            panel.Controls.Add(new Label { Text = "未填写凭证时会跳过官方源并提示；不会使用其他应用的凭证。", AutoSize = true, ForeColor = Color.DimGray });
            panel.Controls.Add(new Label { Text = "自定义兼容 API（最多 5 个，每行：来源名称|API 根地址）", AutoSize = true, Margin = new Padding(0, 18, 0, 5) });
            var custom = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 780, Height = 125, Text = SettingsStore.Unprotect(settings.EncryptedAdditionalApis), WordWrap = false };
            panel.Controls.Add(custom);
            panel.Controls.Add(new Label { Text = "可填写自己部署或可信的动漫弹幕服务，例如：我的动漫源|http://服务器:端口/访问密钥\r\n需兼容 /api/v2/search/anime、/bangumi/{id}、/comment/{id}。配置内容使用 Windows 加密保存。", AutoSize = true, ForeColor = Color.DimGray });
            var status = new Label { AutoSize = false, Width = 780, Height = 42, ForeColor = Color.DarkRed, Margin = new Padding(0, 8, 0, 0) };
            var save = new Button { Text = "保存并立即启用", AutoSize = true, Height = 36 };
            save.Click += (s, e) =>
            {
                try
                {
                    DanmuCatalog.ValidateAdditionalApis(custom.Text);
                    string id = appId.Text.Trim();
                    if (id.Contains("\r") || id.Contains("\n") || id.Length > 100) throw new ArgumentException("AppId 格式无效。");
                    var before = Json.Read<AppSettings>(Json.Write(settings));
                    try
                    {
                        settings.EnableAnimeko = animeko.Checked; settings.EnableBahamut = bahamut.Checked; settings.EnableExistingDanmu = existing.Checked;
                        settings.EnableDandan = dandan.Checked; settings.AnimeOnly = only.Checked; settings.DandanAppId = id;
                        settings.EncryptedDandanSecret = SettingsStore.Protect(secret.Text.Trim()); settings.EncryptedAdditionalApis = SettingsStore.Protect(custom.Text.Trim());
                        SettingsStore.Save(settings);
                    }
                    catch
                    {
                        settings.EnableAnimeko = before.EnableAnimeko; settings.EnableBahamut = before.EnableBahamut; settings.EnableExistingDanmu = before.EnableExistingDanmu;
                        settings.EnableDandan = before.EnableDandan; settings.AnimeOnly = before.AnimeOnly; settings.DandanAppId = before.DandanAppId;
                        settings.EncryptedDandanSecret = before.EncryptedDandanSecret; settings.EncryptedAdditionalApis = before.EncryptedAdditionalApis;
                        throw;
                    }
                    Log.Write("弹幕来源已保存；电脑和 iPad 下次搜索立即采用新来源。"); DialogResult = DialogResult.OK; Close();
                }
                catch (Exception error) { status.Text = error.Message; }
            };
            panel.Controls.Add(status); panel.Controls.Add(save); Controls.Add(panel);
        }
        static CheckBox Check(string text, bool enabled) { return new CheckBox { Text = text, Checked = enabled, AutoSize = true, Margin = new Padding(0, 0, 0, 9) }; }
    }
}
