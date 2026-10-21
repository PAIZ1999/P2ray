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

## 随包分发的素材

### Twemoji 国旗

- 来源：<https://github.com/twitter/twemoji>
- 许可：**CC-BY 4.0**
- 用途：`v2rayN/v2rayN/Resources/Flags/*.png`（48×48 国旗）

Windows 不渲染区域指示符（U+1F1E6..U+1F1FF）组合，界面里直接写字面国旗 emoji 只会
显示成两个方框，因此改为随包分发位图。由 `scripts/fetch-flags.py` 从 twemoji 的
旗帜 SVG 渲染而来。

### 站点图标

- 来源：<https://ip.net.coffee/> 的 `/favicons/` 静态资源
- 用途：`v2rayN/v2rayN/Resources/Sites/*.png`（24×24 站点图标）

各图标为对应商标持有者的标志，此处仅作「该站点是否可达」的识别用途，不表示任何
关联或背书。由 `scripts/fetch-site-icons.py` 抓取并缩放。

连通性面板的图标必须在本地：那是用户断网时最需要看的界面之一，图标若也要联网取，
恰恰会在最该显示的时候全是破图。

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
