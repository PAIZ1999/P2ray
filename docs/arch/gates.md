---
id: doc:gates
object: 验收门禁（命令 · 判据 · 谁跑 · 不过怎么办）
type: reference
status: active
version: v1.0.0
verified_at: 2026-10-24
updated: 2026-10-24
scope:
  covers: [G1 构建, G2 测试, G3 dist 保留配置, G4 版本与 CI, G5 流量统计实测]
  excludes: [目标与约束 → goal.md, 脚本实现 → scripts/arch-gates.ps1]
criterion: 每条门禁都能被判为 PASS / FAIL / SKIP，且判据是命令输出而不是判断
---

# 门禁

> ★ **谁跑**：`scripts/arch-gates.ps1`（人或 CI）。执行者的口头"跑过了"不算。
> ★ **降级禁止**：不许为了让清单变绿而放宽判据。判据要改，走 ADR。

| # | 门禁 | 命令 | 通过判据 | 不过怎么办 |
|---|---|---|---|---|
| G1 | 可编译 | `dotnet build v2rayN/v2rayN/v2rayN.csproj -c Debug` | 退出码 0，且输出中 `个错误` 前的数字为 0 | 就地修；**不许提交** |
| G2 | 测试全绿且只增不减 | `ServiceLib.Tests.exe` | `失败: 0` 且 `总计 >= 180`（基线写在脚本常量里） | 修到绿；基线只能上调，下调需 ADR |
| G3 | dist 保留用户配置 | `pwsh scripts/test-preserve-config.ps1` | 8 项断言全过，退出码 0 | 修 `build-release.ps1` 的暂存/还原 |
| G4 | 版本与 CI | `Directory.Build.props` 的 `<Version>` + GitHub Actions 最近一次 run | 版本单调递增；当前 HEAD 的 run `conclusion=success`（离线则 SKIP） | 发版前必须绿；红则先修 |
| G5 | 流量统计真有数（真实连接） | `P2ray.exe --selftest`（需可连节点） | `trafficGrew == true` 且 `appDown/clashDown >= 0.9`（Clash 系内核） | 记为 FAIL 并写台账；不许口头解释成"环境问题" |

## CI 侧的覆盖关系（避免重复跑）

| 门禁 | CI 里对应 | 说明 |
|---|---|---|
| G1 构建 | `build win-x64` / `build win-arm64` 的 `Build release` | 同一份 csproj，CI 绿即等价 |
| G2 测试 | `test` job 的 `Run tests` | 同上 |
| G3 dist 保留配置 | `test` job 的 `Verify dist keeps user config` | 已在 CI 里 |
| G4 版本与 CI | 本次 run 自身 | 由 CI 结论判定 |
| G5 统计实测 | **无** | 需要真实节点，只能本地跑 |

★ 所以 CI 已覆盖 G1–G3；本地跑 `arch-gates.ps1` 的意义是**发版前的单点复核**与 G5。

## 五个控制点（每个都要有主人）

| 控制点 | 时机 | 查什么 |
|---|---|---|
| 开工 | 每轮开始 | 读 `next.md`（只应有一个动作）· 复述目标一句话 |
| 写前 | 改文件前 | 改动是否在 `goal.md` 的 ③ 约束内；是否碰到只读区（证据 / 用户数据） |
| 写后 | 产物落盘 | 命名 ASCII、无 BOM、门禁能跑 |
| 收工 | 每轮结束 | 跑 `arch-gates.ps1`，把结论写进台账；**没跑不许说完成** |
| 完成 | 交付/发版 | 只有 G1–G4 全 PASS 才能打 tag |

## 完成声明门（五步）

```
① 说清：哪条命令 / 哪个产物能证明它？
② 真跑：本会话跑完整命令（不拿上次输出）
③ 读全：退出码、失败数、关键行
④ 对照：输出支持这个声明吗？不支持 ⇒ 报实际状态
⑤ 才说：带证据说"完成"
```

## 已知的判据弱点（如实记录）

- **G5 依赖外部节点**：没有可用节点时只能 SKIP，这时的"统计正常"没有证据。
- **G2 的 `总计 >= 180` 只能防倒退，不能防"删测试"**：删掉 5 个再新增 5 个仍为 180，需要人抽查。
- **G4 依赖 GitHub API**：无网络时 SKIP；SKIP 不等于通过。
- **界面类改动无法被这五条覆盖**：渲染/交互仍需人眼看截图，门禁只能保证不崩。
