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

**把 G5 变成可重复的判据**：现在它依赖"手上有可用节点"，节点不通时只能 SKIP。

具体做法：在 `scripts/arch-gates.ps1` 里给 G5 增加一个 `-UseLocalSocks <port>` 选项，
直接用本机已运行的代理端口造流量，不依赖 P2ray 自己连接；连不上就明确 SKIP 并写原因。

判据不变：`trafficGrew == true` 且 `appDown/clashDown` 在 0.9–1.1。