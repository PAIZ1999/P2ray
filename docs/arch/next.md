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

**等批准 ADR-0004（Xray 按域名 = 连接次数），期间不做产品改动。**

批准后第一个动作：在 `AppEvents` 与 `TrafficViewModel` 里加"内核决定口径"的分支，
`按域名` 页在 Xray 下改读 access log（连接次数），列头显示「连接次数」而不是「流量」。

未批准则本单元收口，不实现。