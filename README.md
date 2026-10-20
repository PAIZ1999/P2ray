# P2ray

Windows 桌面代理客户端。[Xray-core](https://github.com/XTLS/Xray-core) 与 [sing-box](https://github.com/SagerNet/sing-box) 双内核，卡片式界面。

> 本项目基于 [v2rayN](https://github.com/2dust/v2rayN)（GPL-3.0）二次开发，遵循 GPL-3.0 发布。

---

## 下载

从 [Releases](https://github.com/PAIZ1999/P2ray/releases) 获取最新版本。

| 平台 | 文件 |
|---|---|
| Windows x64 | `P2ray-windows-64.zip` |
| Windows arm64 | `P2ray-windows-arm64.zip` |

解压后直接运行 `P2ray.exe`，内置代理内核，无需另装。

每个包附带 `.sha256` 校验文件：

```powershell
Get-FileHash .\P2ray-windows-64.zip -Algorithm SHA256
```

---

## 功能

- 支持 VLESS / VMess / Shadowsocks / Trojan / Hysteria2 / TUIC / Anytls / WireGuard 等协议
- 订阅导入与自动更新
- 系统代理与 TUN 模式
- 实时速率曲线
- 节点延迟测试与分组管理

---

## 从源码构建

需要 .NET 10 SDK。

```powershell
git clone https://github.com/PAIZ1999/P2ray.git
cd P2ray
pwsh ./scripts/build-release.ps1 -Runtime win-x64
```

产物在 `dist/P2ray-win-x64/`。

构建脚本会从上游官方 Release 拉取内核并钉扎版本，见 `scripts/fetch-cores.ps1`。

### 开发构建

```powershell
dotnet build v2rayN/v2rayN.slnx -c Debug
dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj
```

---

## 项目结构

```
scripts/
  fetch-cores.ps1     拉取并钉扎 Xray-core / sing-box
  build-release.ps1   完整发布构建
  make-icons.py       由 SVG 生成多尺寸图标
  check-contrast.py   配色对比度审计
v2rayN/
  ServiceLib/         业务逻辑（协议、订阅、配置生成、系统代理）
  ServiceLib.Tests/   单元测试
  v2rayN/             WPF 界面
  AmazTool/           自更新辅助进程
```

品牌相关的全部常量集中在 `v2rayN/ServiceLib/ProductInfo.cs`。

---

## 许可

本项目遵循 [GPL-3.0](LICENSE)。

二次开发基于 v2rayN，其版权归原作者 2dust 及贡献者所有，原始版权声明保留于源码中。

随包分发的第三方组件：

| 组件 | 许可 |
|---|---|
| [Xray-core](https://github.com/XTLS/Xray-core) | MPL-2.0 |
| [sing-box](https://github.com/SagerNet/sing-box) | GPL-3.0 |
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | MIT |

完整声明见 [NOTICE.md](NOTICE.md)。

### 源码获取

GPL-3.0 要求分发二进制时提供对应源码。本仓库即完整源码，任何发布版本都能在
[Releases](https://github.com/PAIZ1999/P2ray/releases) 对应 tag 下取得源码归档。
