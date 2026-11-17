# 第三方组件

DanmuCinema 自有代码使用根目录 [MIT 许可证](../../../LICENSE)。第三方代码和二进制保留其许可证及版权声明。

| 组件 | 版本 | 来源 |
| --- | --- | --- |
| Electron | 45.0.0 | https://github.com/electron/electron |
| React / React DOM | 19.3.1 | https://github.com/facebook/react |
| liquid-glass-react | 1.1.1 | https://github.com/rdev/liquid-glass-react |
| Motion | 14.6.0 | https://github.com/motiondivision/motion |
| Lucide React | 1.61.0 | https://github.com/lucide-icons/lucide |
| Jellyfin Server / Web | 12.1 | https://github.com/jellyfin/jellyfin · https://github.com/jellyfin/jellyfin-web |
| Jellyfin-FFmpeg | 8.1.2-1 | https://github.com/jellyfin/jellyfin-ffmpeg |
| Jellyfin.Plugin.Danmu | 2.8.0 | https://github.com/cxfksword/jellyfin-plugin-danmu |
| mpv | 0.42.0-1-g3138fc8ee | https://github.com/mpv-player/mpv · https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261105 |
| ModernZ | 0.3.3，提交 579897e8 | https://github.com/Samillion/ModernZ |
| JASSUB / libass | 2.5.37，依赖提交见源码清单 | https://github.com/ThaUnknown/jassub · https://github.com/libass/libass |
| OpenCC JS | 版本与来源见 electron/vendor/opencc-source.json | https://github.com/nk2028/opencc-js |
| Noto Sans SC | 捆绑字体，OFL | https://github.com/notofonts/noto-cjk |
| Inno Setup（构建工具） | 6.7.3 | https://jrsoftware.org/ |

JavaScript 依赖版本由 `package-lock.json` 固定。Jellyfin 使用 GPL-2.0，Danmu 插件使用 GPL-3.0；其他组件以各自随附许可证为准。安装目录 `licenses` 保存许可证及合并的必要声明，Electron 与 Jellyfin 原有声明随运行组件分发。

Release 的 `DanmuCinema-Corresponding-Sources` 归档包含本项目源码及捆绑组件对应源码，包括 Jellyfin Server/Web、Jellyfin-FFmpeg、Danmu、FFmpeg 静态依赖、shaderc 依赖、libplacebo 子模块、LLVM 运行库和相关 .NET 库。上游构建脚本与补丁随源码保存。

发布时同时提供安装包、对应源码包和校验文件。普通使用只需安装 EXE。安装包不分发构建工具，不包含个人配置或媒体数据。
