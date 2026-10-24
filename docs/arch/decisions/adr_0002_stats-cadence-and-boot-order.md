---
id: adr:0002
object: 统计采集的节奏与启动顺序
type: decision
status: active
version: v1.0.0
verified_at: 2026-10-24
updated: 2026-10-24
decided: 2026-10-24
scope:
  covers: [轮询/落盘/界面刷新三档节奏, 为什么 StartPolling 必须排在数据库操作之前]
  excludes: [按应用的数据源 → adr_0001]
criterion: 后人调快慢时知道代价；再看到"统计全 0"时先查启动顺序
---

# ADR 0002 · 统计采集的节奏与启动顺序

## 背景

两个独立问题，根因不同：

1. 用户反馈"流量统计不是实时的" —— 概览最快 20 秒才变一次。
2. 更严重：**v1.6.0–v1.10.0 期间统计整体失效**（内核走了 4MB，库里 0 增长）。

## 问题 2 的根因（先修）

`StatisticsManager.Init` 里三个 `StartPolling` 排在数据库初始化之后：

```csharp
await TrafficStatService.Instance.LoadCurrentHourAsync();   // 数据库异步
StatisticsRetentionHandler.PurgeExpired();                  // 当时是同步阻塞版
TrafficStatService.Instance.StartPolling();                 // ← 执行不到
```

`PurgeExpired` 用 `.GetAwaiter().GetResult()` 同步等数据库，卡住后三个轮询器全都不启动，
于是概览、按域名、按应用**一起失效**。这个顺序问题是 v1.6.0 引入保留策略时埋下的。

## 决策

1. **先起采集，再做数据库初始化**：三个 `Start()` 提到最前；注释写明顺序原因，防止被挪回去。
2. `PurgeExpiredAsync`：全异步，删掉同步阻塞实现。
3. 节奏：轮询 **2 秒** / 落盘 **5 秒** / 页面刷新 **3 秒**（定时器放视图层，ServiceLib 不引用 WPF）。

## 证据

| 项 | 数据 |
|---|---|
| 修复前 | 内核 +4,016,940；数据库 +0 |
| 修复后 | 内核 +4,016,854；数据库 +4,013,834（差 0.07%） |

## 代价

- 落盘从 20 秒缩到 5 秒：数据库写入频率提高 4 倍。目前量级（单机、每天几十行）可忽略；
  若将来统计表变大，需要重新评估（分批写 / 内存聚合更久）。
- 页面每 3 秒刷新一次：仅在页面可见且不在刷新中时执行，可接受。

## 已知局限

- 采样式统计在"内核启动前"的那段流量不会计入（可接受，属预期）。
- 若宿主把数据库操作放在 UI 线程上同步等待，仍可能出现停住 —— 本项目已多次踩到，
  规矩是：**数据库异步调用不要从 UI 线程发起同步等待**。
