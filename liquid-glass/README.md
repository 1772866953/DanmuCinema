# 开发与构建

## 目录

| 目录 | 内容 |
| --- | --- |
| `src`、`public` | React 页面、控件、样式、图标 |
| `electron` | 窗口、托盘、开机启动、IPC 与进程管理 |
| `development/src`、`development/engine` | 媒体、弹幕、缓存、定时及无界面控制核心 |
| `development/plugin` | Jellyfin 播放准备与媒体分类插件 |
| `installer` | Inno Setup 安装与卸载脚本 |
| `scripts`、`development/scripts` | 构建、打包与检查脚本 |
| `tests`、`development/tests` | 接口、匹配、分类及媒体库测试 |
| `licenses` | 第三方许可证 |

构建产物、下载依赖、运行数据和测试输出不进入 Git。旧版 WPF 界面不属于当前源码。

## 环境

- Windows x64，.NET Framework 4.8。
- Node.js 22.12 或更新的受支持版本、npm。
- PowerShell 7.6 或更新版本，需包含 .NET 10 和 Roslyn 编译程序集。
- 打包时另需 Python 3、Inno Setup 6.7.3。将 `ISCC.exe` 加入 PATH，或设置环境变量 `DANMU_ISCC` 为其完整路径。

以下命令均在本目录执行：

```powershell
npm ci
pwsh -NoProfile -File scripts/prepare-dependencies.ps1
pwsh -NoProfile -File scripts/build.ps1
npm start
```

依赖准备脚本下载固定版本的 Jellyfin 与弹幕插件，校验 SHA-256，并从 npm 依赖复制 Electron 运行时。已有且校验通过的归档直接复用。离线环境可按脚本内文件名将归档放入 `development/downloads`。

默认依赖目录为 `development`；可用 `DANMU_DEPENDENCY_ROOT` 指向共享目录。其下包含 `downloads` 和 `bin/runtime-references/jellyfin`。本机存在工作区归档目录时会自动复用，公开构建不需要旧 UI 源码。

`scripts/build.ps1 -SkipRuntime` 仅编译代码，不复制媒体运行组件。插件通过 Jellyfin 运行包中的程序集编译；桌面业务核心使用系统 .NET Framework 编译器。

## 生成安装包

```powershell
pwsh -NoProfile -File scripts/prepare-dependencies.ps1 -ForInstaller
pwsh -NoProfile -File scripts/build-installer.ps1 -Version 2.0.4
```

`-ForInstaller` 从同版本 Release 获取第三方源码及许可证归档，保存在 `development/dependencies/redistribution-sources.zip`。首次发布新版本时，应由维护者提前准备完整的第三方归档，再执行打包，不能依赖尚不存在的 Release。

输出位于仓库根目录 `packages`：EXE、对应源码 ZIP 及各自 SHA-256 文件。二进制与大型源码归档应上传 Release，不提交 Git。安装器会创建空 API 配置；不得将本机配置、加密密钥、账号、缓存、日志或媒体文件加入公开包。

## 检查

完成构建后，可按修改范围选择检查：

```powershell
npm test
pwsh -NoProfile -File scripts/test-core.ps1 -Case MatchingRegression
pwsh -NoProfile -File scripts/test-core.ps1 -Case ClassificationRegression
pwsh -NoProfile -File scripts/test-core.ps1 -Case LibraryRemovalRegression
```

界面检查另需 `npm install --no-save --package-lock=false playwright@1.62.1`。例如 `node scripts/check-library-removal-ui.mjs`。检查使用独立数据目录、模拟接口或合成媒体；实文件哈希探测须显式指定安装目录和视频。测试结果不能替代真实客户端播放验证。

## 数据与通信

开发模式使用独立的 `runtime-data`。安装版使用安装目录下的 `data`、`config`；两者不要共用账号数据库，也不要同时占用相同服务端口。

Electron 渲染进程启用沙盒和上下文隔离，禁用 Node.js；主进程校验 IPC 来源及方法。业务核心通过继承的标准输入输出管道通信。关闭到托盘时销毁界面窗口，恢复时重新创建；媒体服务与定时任务继续运行。
