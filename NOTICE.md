# NOTICE

P2ray 包含或分发以下第三方组件。各组件版权归其原作者所有，适用其各自许可。

---

## 本项目

**P2ray** — GPL-3.0

基于 **v2rayN**（<https://github.com/2dust/v2rayN>）二次开发。

v2rayN 版权归 2dust 及贡献者所有，原始版权声明保留于各源文件头部与
`v2rayN/ServiceLib/Resx/` 等资源中。本项目的修改同样以 GPL-3.0 授权。

---

## 随包分发的运行时组件

### Xray-core

- 来源：<https://github.com/XTLS/Xray-core>
- 版本：见 `scripts/fetch-cores.ps1` 中的钉扎值（当前 v26.3.27）
- 许可：**MPL-2.0**

以独立可执行文件分发（`bin/xray/xray.exe`），未修改其源码，
按 MPL-2.0 要求提供其源码获取地址。

### sing-box

- 来源：<https://github.com/SagerNet/sing-box>
- 版本：见 `scripts/fetch-cores.ps1` 中的钉扎值（当前 v1.14.3）
- 许可：**GPL-3.0**（附额外条款）

以独立可执行文件分发（`bin/sing_box/sing-box.exe`），未修改。

> 注：GitHub 将其许可标记为 `NOASSERTION`，因为 LICENSE 正文在 GPL-3.0
> 基础上附加了条款。此处按 LICENSE 实际文本记录为 GPL-3.0 并注明存在附加条款，
> 未做简化。

---

## 编译期依赖（.NET 包）

| 包 | 许可 |
|---|---|
| MaterialDesignThemes | MIT |
| ReactiveUI / ReactiveUI.WPF | MIT |
| H.NotifyIcon.Wpf | MIT |
| Avalonia 系（已移除，不再使用） | — |

完整依赖树见 `v2rayN/Directory.Packages.props`。

---

## 源码获取

本仓库即完整源码。任何发布版本的对应源码可在
`https://github.com/PAIZ1999/P2ray/releases/tag/<tag>` 取得源码归档。

GPL-3.0 要求分发二进制时提供对应源码，本仓库满足该要求。
