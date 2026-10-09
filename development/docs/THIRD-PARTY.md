# 第三方组件

此项目是本地控制台与运行组件的集成，不重新实现 Jellyfin 或上游视频站的弹幕采集器。

DanmuCinema 独立编写的源码及脚本使用根目录 [MIT 许可证](../../LICENSE)。以下第三方组件保留原许可证；源码仓库不包含其运行二进制，离线 EXE 安装包内置这些组件，无需安装时另行下载。

| 组件 | 固定版本 | 官方来源 | 源码与许可证 |
|---|---|---|---|
| Jellyfin Server | 12.1 | https://repo.jellyfin.org/files/server/windows/latest-stable/amd64/jellyfin_12.1-amd64.zip | https://github.com/jellyfin/jellyfin/tree/v12.1 · GPL-2.0，保存于 LICENSE-Jellyfin.txt |
| Jellyfin.Plugin.Danmu | 2.8.0.0 | https://github.com/cxfksword/jellyfin-plugin-danmu/releases/tag/v2.8.0 | https://github.com/cxfksword/jellyfin-plugin-danmu/tree/v2.8.0 · GPL-3.0，保存于 LICENSE-Danmu.txt |

Jellyfin 运行包包含 FFmpeg 和其他组件；相应第三方声明保留在原始运行包目录中。Danmu 的来源支持、接口稳定性和自动匹配规则由该插件决定。

Jellyfin 插件说明：https://github.com/cxfksword/jellyfin-plugin-danmu

Windows 桌面控制台使用系统自带 .NET Framework 4.8 / WPF，自定义主题不依赖额外 UI 工具包。离线包支持 Windows 10 2004 或更新版本及 Windows 11 的 64 位系统。Jellyfin 自带其所需运行时，FFmpeg 版本为 8.1.2-Jellyfin；桌面源码位于 desktop，业务代码及兼容回退界面位于 src，托盘与文件夹选择使用 Windows Forms。

安装后的 licenses 目录仅保留 LICENSE 文件，原版权声明与 NOTICE 内容合并到对应许可文本中。完整源码独立分发，不放入安装目录。对应来源：https://github.com/jellyfin/jellyfin-web/tree/v12.1 、https://github.com/jellyfin/jellyfin-ffmpeg/tree/v8.1.2-1 。

公开发布时应同时提供同版本 DanmuCinema-Corresponding-Sources ZIP，其中包含本次控制台及桥接插件源码、Jellyfin Server/Web、Jellyfin-FFmpeg、Danmu、FFmpeg 静态依赖、shaderc DEPS 依赖、libplacebo 子模块、LLVM 运行库及 BDInfo/TagLibSharp/UTF.Unknown 源码。依赖来源和校验值保留于源码清单，上游构建文件与补丁保持完整。自有源码快照排除说明文档和个人配置。用户运行仅需安装 EXE，无需下载源码或插件。NuGet 许可材料按运行包实际依赖版本收集，原二进制始终使用经校验的 Jellyfin 官方运行包。

安装器由 Inno Setup 6.7.3 构建（https://jrsoftware.org/）；简体中文安装器翻译沿用 Inno Setup 官方仓库的 ChineseSimplified.isl，保留译者声明。安装包、构建工具、运行数据不进入 Git；包构建采用文件白名单，不包含真实 API 配置、账号、媒体文件或缓存。
