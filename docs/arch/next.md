---
id: doc:next
object: 下一动作（★ 只准一条）
type: plan
status: active
version: v1.0.0
verified_at: 2026-10-24
updated: 2026-10-24
scope:
  covers: [唯一的下一动作]
  excludes: [目标 → goal.md, 判据 → gates.md]
criterion: 读完知道下一步做什么，且只有一件事
---

# 下一动作

**查清「数据库异步调用在 UI 线程上挂起」到底是不是产品缺陷。**

这个现象在本项目已出现 4 次（订阅更新、内核版本读取、自检连接、设置页初始化），
每次都是"文件写完了、数据库那步没回来、日志里也没有异常"。

做法：在应用启动时于 **UI 线程** 执行一次
`SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync()`，用 5 秒看门狗记录耗时，
结果（成功 / 超时 / 异常）写到 `guiLogs` 里的一行；连跑两次确认是否稳定复现。

判据：同一台机器上连续两次都超时 ⇒ 是产品缺陷，按 P0 修（把这类调用移出 UI 线程）；
只有一次超时 ⇒ 记为偶发，加超时保护即可，不扩大改动。