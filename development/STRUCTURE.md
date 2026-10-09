# 工作区目录

根目录保留 development/、packages/，以及仓库必需的 README、LICENSE、忽略规则和 Git 元数据。

- development/：源码、图标、安装器、脚本、测试及第三方许可。bin/installer-app 是唯一程序构建输出；bin/runtime-references 只保存离线提取的编译引用；bin/reports 保存验证报告。downloads 保存离线组件和源码。
- packages/：可分发的安装 EXE、对应源码 ZIP 和 SHA256。公开发布时同时提供安装包和对应源码包。

构建程序：powershell -NoProfile -ExecutionPolicy Bypass -File development/scripts/build.ps1
构建安装包：powershell -NoProfile -ExecutionPolicy Bypass -File development/scripts/build-installer.ps1
构建机器需要 Inno Setup、Python 3.9+ 和 PowerShell 7.6+；安装使用者无需下载额外组件。

运行软件请使用 EXE 安装包安装。工作区不再维护独立的本机运行环境或个人 API 配置。
卸载时账号和 API 配置会删除；保留缓存和日志记录默认勾选。媒体文件及其旁边的弹幕不参与卸载清理。
