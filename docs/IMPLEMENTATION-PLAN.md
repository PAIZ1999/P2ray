# P2ray 实施状态

基于 [v2rayN](https://github.com/2dust/v2rayN) 7.25.6（GPL-3.0）二创的 Windows 桌面代理客户端。P2ray 自身版本从 1.0.0 起算，与上游版本号解耦。

> 本文档记录**实际完成状态与验证证据**，不是计划稿。规划期的跨平台（Avalonia/macOS）方案已作废。

## 一、产品定位

| 项 | 决定 |
|---|---|
| 品牌 | P2ray |
| 平台 | **仅 Windows**（x64 / arm64） |
| UI | WPF + MaterialDesignThemes，深色主题（对齐 PiFlow 视觉语言） |
| 许可 | GPL-3.0（继承上游） |
| 更新 | 自有 GitHub 仓库 Release |

## 二、完成状态

### 阶段 0：环境与骨架 ✅

- .NET 10 SDK（工作区本地目录，不污染系统）
- 测试基线：136 项全绿

### 阶段 1：白标底座 ✅

品牌常量集中在 `v2rayN/ServiceLib/ProductInfo.cs`，换品牌只需改这一个文件。

| 改动点 | 文件 |
|---|---|
| 品牌单一来源 | `ServiceLib/ProductInfo.cs` |
| 应用名 / 配置名 / URL scheme / 自启项 / 更新仓库 | `ServiceLib/Global.cs` |
| 数据目录 `%LOCALAPPDATA%\P2ray` | `ServiceLib/Common/Utils.cs` |
| 数据库 `p2rayDB.db` | `ServiceLib/Helper/SqliteHelper.cs` |
| 自更新辅助进程名 | `AmazTool/*` |
| 窗口标题与图标 | `v2rayN/Views/MainWindow.xaml`、`Resources/` |
| 移除 Avalonia 项目与 GlobalHotKeys 子模块 | `v2rayN.slnx`、`.gitmodules` |
| 移除 WebAPI 与 Linux/macOS 打包脚本 | 仓库根目录、`.github/workflows/` |

**图标**：由 `scripts/make-icons.py` 从 SVG 几何重绘，生成 7 尺寸 ico（16–256）与 4 个托盘图标。

### 阶段 2：内核分发 ✅

| 内核 | 版本（钉扎） | 布局 |
|---|---|---|
| Xray-core | v26.3.27 | `bin/xray/xray.exe` |
| sing-box | v1.14.3 | `bin/sing_box/sing-box.exe` |
| geo 数据 | 随 Xray 包 | `bin/geoip.dat`、`bin/geosite.dat` |
| sing-box 规则集 | 4 个 `.srs` | `bin/srss/` |

脚本：
- `scripts/fetch-cores.ps1` — 从官方 Release 拉取，版本钉扎，输出 sha256
- `scripts/prefetch-rulesets.py` — 预置 sing-box 规则集，使内核离线可用
- `scripts/build-release.ps1` — 完整发布构建

**关键约束**：geo 数据必须放在 `bin/` 根目录（`XRAY_LOCATION_ASSET` 指向此处）。放错子目录会导致 Xray 启动后立即退出，而应用只报"运行 Core 失败"这类含糊错误。

**关键约束**：sing-box 若没有本地规则集，会退化为远程引用并在启动时同步下载，失败即退出。对无法直连 GitHub 的用户等于内核不可用。

### 阶段 3：UI 重做 ✅

三项导航（首页 / 节点 / 设置），替换上游 4 套可切换布局。

| 视图 | 内容 |
|---|---|
| `DashboardView` | 连接主卡（状态+节点+按钮）、四项指标（上下行速率及曲线、入站、核心与运行时长）、快捷操作条、订阅列表、运行日志 |
| `NodesView` | 自绘列表（名称/地址/协议/延迟/分组）、默认徽章、右键菜单、双击设为默认、Delete 删除、多选 |
| `SettingsView` | 系统代理、TUN、订阅管理、路由、核心设置、数据目录、关于 |

主题：`Themes/P2rayTokens.xaml` 定义全部设计令牌，颜色硬编码（避免 `DynamicResource` 静默退化）。
字体渲染：`Grayscale` + `Ideal`（深色背景下 ClearType 会产生彩色描边）。
对比度：按 WCAG 公式实测，三级文字 `#8B8B8B`（5.26:1），禁用文字 `#666666`。

### 阶段 4：交付与更新 ✅（除仓库地址）

- `.github/workflows/build.yml`：test → build(win-x64/arm64) → tag 触发 Release
- 资产名与代码常量对齐（`P2ray-windows-64.zip`）
- `README.md`、`NOTICE.md`（含 sing-box 许可的如实记录）

## 三、验证证据

| 项 | 结果 |
|---|---|
| 构建 | 成功（`NU1900` 警告为本机无 nuget.org 网络，非代码问题） |
| 单元测试 | 136 / 136 |
| 发布包 | 473 文件 / 349.8 MB |
| Xray 端到端 | 真实 VLESS+Reality 节点，HTTP 204 |
| sing-box 端到端 | HTTP 301，离线规则集生效 |
| 订阅解析 | 实测导入，VLESS+Reality+Vision 参数完整 |
| XAML 合法性 | 30 文件 0 错误 |
| 事件引用完整性 | 31 处 XAML→代码引用，0 缺失 |

## 四、修复的上游缺陷

1. **`ProfileExManager` 竞态** — 上游 #10388 的提交说明写明要给节点查找加锁，实际 diff 只换了队列类型。并发测速会产生重复统计行。已加锁。
2. **`ConfigHandler.SetDefaultRouting` 空引用** — `RoutingItems()` 声明为可空却直接调 `.Any()`，每次冷启动无路由数据时抛异常。已加防护。
3. **`UpdateService.GetRemoteVersion` 误报更新** — 无条件返回 `Success=true`，仓库不可达（404）时界面仍显示"有更新"。已加版本解析校验。

## 五、待完成

**唯一阻塞项**：`ProductInfo.GithubRepo` 为占位值 `owner/P2ray`。（已完成：已改为 `PAIZ1999/P2ray`，README / NOTICE 链接同步更新。）

替换后需执行：

1. 改 `v2rayN/ServiceLib/ProductInfo.cs` 的 `GithubRepo` 为真实 `owner/repo`
2. 更新 `README.md` 与 `NOTICE.md` 中的链接
3. 推送仓库，打 tag（如 `v1.0.0`），CI 自动构建并创建 Release

在此之前，自更新会请求不存在的仓库并返回 404（已确认不会误报"有更新"）。

## 六、附录：本机构建环境的坑

两个问题都源于本机为 **Windows 10 19045.2132**（缺微软后续 CET 支持更新），上游 CI 跑 Windows Server，从未暴露。

1. **类库项目缺 `CETCompat`**：上游只在两个 UI 项目设置。已在 `Directory.Build.props` 提升为全局。
2. **Roslyn / MSBuild apphost 带 CETCOMPAT 标记**：`csc.exe`、`VBCSCompiler.exe`、`MSBuild.exe`、`apphost.exe` 的 PE 调试目录含 type 20 条目值为 `0x0001`，coreclr 检测到即 Fatal。已对工作区 SDK 副本清除标记（仅本地，不入库）。

`PublishTrimmed=true` 会触发 ILLink 任务，需要 MSBuild 任务宿主，因此也依赖上述修复。
