# 动漫弹幕来源

2026-10-06 增加了 Animeko、巴哈姆特动画疯直接适配器，并支持弹弹play官方签名认证及多个自定义 v2 API。适配器由本项目独立实现，不执行第三方爬虫项目代码。

## 实测

通过与 iPad 相同的 HTTP API 流程查询「骸骨骑士」：

| 来源 | S2 目录 | 集数 | 第 3 集弹幕 |
|---|---|---|---|
| Animeko | 骸骨骑士大人异世界冒险中 第二季，2026 | 12 | 9 条 |
| 巴哈姆特动画疯 | 骸骨骑士大人异世界冒险中 第二季，2026 | 12 | 544 条 |

搜索、番剧详情、选集、XML 和 JSON 弹幕均通过网关验证。只在 `tests/output` 保存了测试 XML，没有自动给真实视频关联弹幕。

结果反映测试时的上游数据，并不保证每部动漫、每一集都有弹幕。Animeko 搜索来自 Bangumi 目录，界面会提示这是目录候选；实际数量需下载确认。不同来源不自动混合弹幕，以便用户核对剪辑与季度。

## 来源设置

「影片与弹幕」→「弹幕来源」。默认同时启用 Animeko、巴哈姆特和原有平台。弹弹play 仅保留来源开关，凭证填写在本机 `config/dandanplay.json`，未就绪不阻断其他来源。最多可以加入 5 个自定义兼容源。智能搜索和本季批量下载见 [使用说明](BATCH-AND-MATCHING.md)。

每行格式：`来源名称|API 根地址`。例如自己的服务 `我的动漫源|http://服务器:端口/访问密钥`。该地址必须支持：

- `GET /api/v2/search/anime?keyword=...` → `success, animes[]`
- `GET /api/v2/bangumi/{animeId}` → `success, bangumi.episodes[]`
- `GET /api/v2/comment/{episodeId}?format=json` → `comments[]`，每条包含 `p` 和 `m` 或 `text`

自定义 API 配置使用 DPAPI 加密。旧版官方密钥迁移时保留加密；独立配置文件也支持手动填写 AppSecret，真实文件不会上传。网关不把 Jellyfin 凭证发送给外部来源。外部请求遵循 Windows 系统代理，来源超时单独报告。

## 官方资料

- [Animeko 官方项目及公益弹幕介绍](https://github.com/open-ani/animeko)
- [Bangumi API 官方文档](https://bangumi.github.io/api/)
- [巴哈姆特动画疯](https://ani.gamer.com.tw/)
- [弹弹play 官方接入说明](https://doc.dandanplay.com/open/)
- [弹弹play v2 API 文档](https://api.dandanplay.net/swagger/index.html)

## 验证

自检覆盖来源并发、单源错误隔离、动漫过滤、来源 ID 区分、缓存恢复、XML 格式转换，以及实际本机 HTTP 网关调用。真实源测试运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-anime-sources.ps1 -Executable DanmuCinema.exe
```

它只将公开弹幕样本保存到项目测试目录。报告在 `tests/output/anime-sources-live.txt`。
