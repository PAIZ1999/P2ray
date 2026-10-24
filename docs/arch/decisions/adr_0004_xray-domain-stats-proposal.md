---
id: adr:0004
object: Xray 下「按域名」能否做 —— access log 实测可行，但只能给「连接次数」
type: decision
status: proposed
version: v0.1.0
verified_at: 2026-10-24
updated: 2026-10-24
decided: null
scope:
  covers: [实测数据, 判据核对, 三种可选方案, 需要谁批准]
  excludes: [实现细节（未批准前不写）, clash 内核的口径 → adr_0001]
criterion: 批准人能在 3 分钟内决定"做 / 不做 / 只做连接次数"
---

# ADR 0004（提案）· Xray 按域名：可行，但口径是「连接次数」

> **状态：提案，未实现。** 按 `goal.md` ③ 的规矩，改变产品能力范围需要批准。

## 背景

「按域名」「按应用」当前依赖 clash API，Xray 内核下为空（见 ADR-0001、`goal.md` 已知限制）。
`next.md` 上一版要求先验证：Xray 的 access log 能否提供「来源端口 + 目标域名」。

## 实测（Debug 配置，Xray 内核，日志级别 info，跑约 25 秒）

把 `CoreBasicItem.LogEnabled=true`、`Loglevel=info`，生成 `guiLogs/Vaccess_2026-10-24.txt`，
内容是这样（原文三行，未删改）：

```
2026/10/24 17:05:10.891071 from tcp:127.0.0.1:11494 accepted tcp:www.google.com:443 [socks -> proxy]
2026/10/24 17:05:12.323959 from tcp:127.0.0.1:11498 accepted tcp:speed.cloudflare.com:443 [socks >> proxy]
2026/10/24 17:05:31.588331 from tcp:127.0.0.1:11519 accepted tcp:www.gstatic.com:443 [socks -> proxy]
```

## 判据核对（判据写在上一版 next.md 里，先定后测）

| 判据 | 结果 |
|---|---|
| ① 每行都有 `from <ip:port>` 与目标 host | **成立**，三行都有 |
| ② 同一域名多连接能否区分 | **成立**：按端口区分，每行一条连接 |
| ③ 量级是否 ≤200 行/分钟 | **成立**：3 行 / 约 25 秒 ≈ 7 行/分钟 |

结论：三条全中 ⇒ 值得做。

## 但有一个口径问题必须先解决

access log **不含字节数**。Xray 自己的 stats API 只有入站/出站/用户三种粒度，也没有逐连接字节。
所以 Xray 下能得到的只有：

```
example.com   12 次连接
www.google.com 3 次连接
```

而 sing-box / mihomo 下「按域名」给的是**字节数**。同一张表在两种内核下单位不同。

## 三种可选方案

| 方案 | 做法 | 代价 |
|---|---|---|
| **A（推荐）** | Xray 下该页显示「连接次数」，列头与单位随内核切换并明示 | 同一页面两种口径，用户需要看列头 |
| B | 只做「连接次数」，所有内核统一改成次数 | 丢掉已有的字节能力，用户会不满 |
| C | 不做 | Xray 用户始终没有按域名 |

## 需要批准的事

1. 选 A / B / C 哪一个。
2. 若选 A：列头文案是否接受「连接次数」而不是「流量」。
3. 是否需要同时把 access log 的开启做成「按域名页可见时自动开启、离开即关闭」
   —— 因为 info 级别日志会写盘（实测很轻，但仍是持续写）。

## 已知局限

- 单次测量、单机、25 秒；长时间高并发下的行数没测。
- access log 只记录 `accepted`，被路由拒绝/直连的连接是否也进日志未验证。
- 若用户把日志目录指向网络盘，写盘成本会变。
