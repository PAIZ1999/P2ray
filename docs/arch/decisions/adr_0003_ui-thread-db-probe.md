---
id: adr:0003
object: 「UI 线程上等数据库会挂起」不成立 —— 不按 P0 修，改的是调用姿势
type: decision
status: active
version: v1.0.0
verified_at: 2026-10-24
updated: 2026-10-24
decided: 2026-10-24
scope:
  covers: [探针方法与数据, 结论, 对既有四起"挂起"的重新解释, 后续规矩]
  excludes: [统计节奏与启动顺序 → adr_0002]
criterion: 下次再遇到"数据库不回来"，先跑 --db-probe 再下结论
---

# ADR 0003 · UI 线程等数据库不挂起（探针结论）

## 背景

本项目先后 4 次出现「文件写完了、数据库那步不回来、日志里也没有异常」：
订阅更新（`AppManager.SubItems`）、内核版本读取、自检连接、设置页初始化。
每次我都换一条路绕过去，从没定性。`docs/arch/next.md` 曾把"是不是产品缺陷"列为待查项。

## 方法

新增 `--db-probe` 启动参数（`v2rayN/Diagnostics/UiThreadDbProbe.cs`，只写日志不改行为）：
在 **UI 线程** 连跑两次同一个查询（`SQLiteHelper.TableAsync<ProfileItem>().ToListAsync()`），
再用 **线程池** 跑一次作对照，各自 5 秒看门狗。

## 数据（同一台机器，连续两次启动）

| 启动 | ui-1 | ui-2 | pool |
|---|---|---|---|
| 第 1 次 | ok，20 ms，6 行 | ok，1 ms，6 行 | ok，0 ms，6 行 |
| 第 2 次 | ok，25 ms，6 行 | ok，0 ms，6 行 | ok，1 ms，6 行 |

**零超时。** 按 `next.md` 事先写死的判据：只有偶发才加超时保护、连续两次都超时才按 P0 修 ——
这里两者都不成立，说明 UI 线程上的异步数据库调用本身没问题。

## 结论

1. **不按 P0 修**。不存在"UI 线程等数据库必挂"这个缺陷。
2. 既有 4 次挂起的共同点不是"UI 线程"，而是**调用姿势**：都是我用
   `Dispatcher.BeginInvoke(new Action(async () => ...))` 这类 **async void** 注入代码发起，
   或者在没有 `SynchronizationContext` 的上下文里 await 后被丢弃的 continuation。
   正常产品代码（ViewModel 里的 `async Task` 命令）从未复现。
3. 因此规矩改成两条：
   - **注入式诊断/临时代码一律用 `Task.Run` + 显式回 UI（`Dispatcher.Invoke`）**，禁用 async void；
   - 再遇到"数据库不回来"，**先跑 `--db-probe`**，用数据判断是产品问题还是调用姿势问题，
     不许直接猜。

## 保留物

- `--db-probe` 参数与 `Diagnostics/UiThreadDbProbe.cs` 保留（5 秒看门狗、结果写
  `guiConfigs/uithread-db-probe.log` 与 guiLogs）。它是下次遇到同类症状时的第一件工具。
- 本次没有为此改动任何产品代码路径。

## 已知局限

- 只测了 `ProfileItem` 一张表的简单查询；带 `Where` 的查询、写入、事务没测。
- 只在一台机器、一个时间点测了两次；无法排除"特定数据量/特定磁盘状态下"才出现。
- 探针在应用启动后台跑，若启动阶段本身极慢，结论可能不适用。
