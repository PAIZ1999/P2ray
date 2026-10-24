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

**验证 Xray 下「按域名」的可行性，再决定要不要实现。**

现状：Xray 没有 clash API，所以「按域名」「按应用」在 Xray 内核下为空。
Xray 自己的 stats API 只有入站/出站/用户三种粒度，拿不到域名。

要验证的一件事：Xray 的 **access log**（`loglevel: info`）能否稳定给出
「来源端口 + 目标域名」，且能与 netstat 的端口→进程映射、以及（若可行的）
逐连接字节数关联起来。

做法：把 Debug 配置的日志级别调到 info，连一个节点跑几分钟，抓 access log，
统计三件事：① 每行是否都有 `from <ip:port>` 与目标 host；② 一个域名多连接时能否区分；
③ 日志量级（每分钟行数）。

判据：若 ① 成立且 ③ 在每分钟 200 行以内 ⇒ 值得做（能给出"按域名连接次数"，
字节数另行解决）；否则记为「Xray 下不支持」，写进 goal.md 的已知限制并收口。