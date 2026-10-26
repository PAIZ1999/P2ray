---
id: note:perf-baseline
object: 连接速度基线测量（节点 RTT 与首包/吞吐的关系）
type: evidence
status: active
version: v1.0.0
verified_at: 2026-10-26
updated: 2026-10-26
scope:
  covers: [测量方法, 数据, 归因, 试过但无效的手段]
  excludes: [代码位置 → Diagnostics/PerfConnect.cs]
criterion: 下次有人报"慢"，先用同一套命令测一遍再下结论
---

# 连接速度基线（2026-10-26）

> 起因：用户反馈「用 P2ray 连接节点感觉好慢」。
> 结论：慢在**节点 RTT**，不在 P2ray。下面是支撑这个结论的数据。

## 测量方法（可复现）

```powershell
# 连上节点但保持运行（不造流量，避免自检干扰）
P2ray.exe --perf-connect

# 首包
curl.exe -s -o NUL -w "%{time_starttransfer}" --socks5-hostname 127.0.0.1:11808 https://www.gstatic.com/generate_204

# 吞吐
curl.exe -s -o NUL -w "%{speed_download}" --socks5-hostname 127.0.0.1:11808 "https://speed.cloudflare.com/__down?bytes=5000000"
```

## 数据

| 指标 | 直连 | 走 P2ray | 备注 |
|---|---|---|---|
| TCP 握手到节点 | — | 255 / 307 / 280 ms | 三次 |
| 首包 www.gstatic.com | 0.19 s | **1.46 s** | |
| 首包 www.bing.com | 0.20 s | **1.54 s** | |
| 首包 https://1.1.1.1（跳过 DNS） | 超时（被墙） | 1.73 / 2.04 / 2.35 s | 说明慢与 DNS 无关 |
| 5 MB 下载 | 1.63 MB/s | **0.88 MB/s** | 约 54% |

路由：`tracert` 到 `yy.piflow.xyz`(38.150.33.176) 在第 7 跳还是 16 ms，
第 11 跳（223.120.15.249，移动国际出口）跳到 208 ms，之后 214 ms —— 节点在境外远端。

## 归因（算术能对上）

首包 ≈ 握手链的 RTT 数 × RTT：

```
TCP → 节点        1 RTT
TLS / REALITY     2 RTT
目标 TCP          1 RTT
目标 TLS          2 RTT
                 ─────
                 6 RTT × 0.26 s ≈ 1.56 s   ← 实测 1.46–1.54 s
```

吞吐：单条 TCP 在 260 ms RTT 下受 BDP 限制（窗口约 230 KB），
0.88 MB/s 与直连 1.63 MB/s 的比例符合预期，不是"代理坏了"。

## 试过但无效

| 尝试 | 结果 |
|---|---|
| 开 mux（h2mux，8 连接） | **完全不通**：REALITY + `xtls-rprx-vision` 与 mux 冲突；已回滚 |
| 怀疑 DNS（DoH 走代理） | 用 IP 目标跳过 DNS，TTFB 仍 1.7–2.4 s ⇒ DNS 不是主因 |

## 排除项（P2ray 侧）

- 出站只有 `vless` + `direct`，**没有中转链**；
- 入站 `sniff` 关闭；
- 路由 `DomainStrategy=AsIs`，不逐域名解析；
- 连接从点击到端口监听约 3 s（含应用初始化 ~2 s + 内核启动 ~1 s），与首包无关。

## 有效手段（按收益排序）

1. **换低 RTT 节点**：这是唯一能把首包从 1.5 s 降到 0.5 s 量级的办法。
   当前订阅里只有 `reality-yy` 一个真节点（其余 `192.0.2.x` 是演示占位）。
2. **服务端开 BBR**：改善长肥管道吞吐，客户端无能为力。
3. 减少握手次数（连接复用）：由浏览器/客户端决定，P2ray 只能靠 mux，而 mux 与 vision 不兼容。

## 已知局限

- 单机、单节点、一次性测量；不同时段（晚高峰）可能更差。
- 未与其他客户端做同节点对照（手上没有第二个客户端）。
