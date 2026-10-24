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

**把 G5（离线统计链路）接进 CI**：现在它只能本地跑。

做法：在 `.github/workflows/build.yml` 的 win-x64 job 里，`Build release` 之后加一步，
运行 `scripts/arch-gates.ps1 -OnlyStats`（需在脚本里加这个开关：只跑 G5，复用已构建的产物），
并把 `docs/arch/state/goals/*.tsv` 作为 artifact 上传。

判据不变：`trafficGrew == true` 且库增量 ≥ 1MB。