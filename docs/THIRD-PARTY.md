# 第三方组件

此项目是本地控制台与运行组件的集成，不重新实现 Jellyfin 或上游视频站的弹幕采集器。

DanmuCinema 独立编写的源码及脚本使用根目录 [MIT 许可证](../LICENSE)。以下第三方组件保留原许可证；它们通过安装脚本单独下载，不包含在源码仓库内。

| 组件 | 固定版本 | 官方来源 | 源码与许可证 |
|---|---|---|---|
| Jellyfin Server | 12.1 | https://repo.jellyfin.org/files/server/windows/latest-stable/amd64/jellyfin_12.1-amd64.zip | https://github.com/jellyfin/jellyfin/tree/v12.1 · GPL-2.0，保存于 LICENSE-Jellyfin.txt |
| Jellyfin.Plugin.Danmu | 2.8.0.0 | https://github.com/cxfksword/jellyfin-plugin-danmu/releases/tag/v2.8.0 | https://github.com/cxfksword/jellyfin-plugin-danmu/tree/v2.8.0 · GPL-3.0，保存于 LICENSE-Danmu.txt |

Jellyfin 运行包包含 FFmpeg 和其他组件；相应第三方声明保留在原始运行包目录中。Danmu 的来源支持、接口稳定性和自动匹配规则由该插件决定。

Jellyfin 插件说明：https://github.com/cxfksword/jellyfin-plugin-danmu

Windows 桌面控制台默认使用系统自带 .NET Framework 4.8 / WPF，自定义主题不依赖额外 UI 工具包。桌面源码位于 desktop，复用的业务代码及兼容回退界面位于 src；托盘与系统文件夹选择使用 Windows Forms。依赖安装包、运行数据不进入 Git。若对外分发包含第三方二进制的完整目录，需一并履行第三方开源许可证的源码提供等义务。
