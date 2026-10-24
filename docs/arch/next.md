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

**给 G6（真实链路）补一次 sing-box 内核的复验**，把「按域名字节」这条口径也纳入证据。

现状：G6 只判「概览有没有涨」和「按应用捕获率」，没管按域名。
做法：在 G6 的判据里加一条 —— `clashDownDelta ≥ 1MB` 且按域名表的字节合计 / clash 增量 ≥ 0.9。

判据不放宽：不达标就 FAIL，不许改成"看起来差不多"。