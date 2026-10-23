#!/usr/bin/env python3
"""把新 UI 里的硬编码文案迁到 ResUI（resx）。

做法：
  1. 表里给出 (键名, 中文, 英文)；
  2. 分别写进 ResUI.zh-Hans.resx（中文）与 ResUI.resx（英文，中性回退）；
  3. 把指定 XAML 里 `Text="中文"` / `Content="中文"` / `Header="中文"` 替换成
     `{x:Static resx:ResUI.键名}`。

只替换完全匹配的属性值，不做模糊匹配 —— 文案一旦改了一个字，脚本会报"未命中"，
避免悄悄漏掉。

用法：python tools/i18n_apply.py <表文件.tsv> <xaml...>
表文件格式：键名<TAB>中文<TAB>英文
"""
import sys
import re
import html
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESX_ZH = ROOT / "v2rayN" / "ServiceLib" / "Resx" / "ResUI.zh-Hans.resx"
RESX_EN = ROOT / "v2rayN" / "ServiceLib" / "Resx" / "ResUI.resx"


def load_table(path):
    rows = []
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 3:
            raise SystemExit(f"表格式错误（需要三列）：{line!r}")
        rows.append(tuple(p.strip() for p in parts))
    return rows


def add_resx_entry(text, key, value):
    if f'name="{key}"' in text:
        return text, False
    entry = (
        f'  <data name="{key}" xml:space="preserve">\n'
        f"    <value>{html.escape(value, quote=False)}</value>\n"
        f"  </data>\n"
    )
    idx = text.rindex("</root>")
    return text[:idx] + entry + text[idx:], True


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    table = load_table(sys.argv[1])
    xaml_files = [Path(p) for p in sys.argv[2:]]

    zh_text = RESX_ZH.read_text(encoding="utf-8-sig")
    en_text = RESX_EN.read_text(encoding="utf-8-sig")
    zh_added = en_added = 0
    for key, zh, en in table:
        zh_text, a = add_resx_entry(zh_text, key, zh)
        en_text, b = add_resx_entry(en_text, key, en)
        zh_added += a
        en_added += b
    RESX_ZH.write_text(zh_text, encoding="utf-8")
    RESX_EN.write_text(en_text, encoding="utf-8")
    print(f"resx 新增：zh-Hans {zh_added} 条，英文 {en_added} 条")

    # 长文案优先替换，避免短文案先命中把长句拆开
    ordered = sorted(table, key=lambda r: -len(r[1]))
    total_replaced = 0
    for path in xaml_files:
        raw = path.read_text(encoding="utf-8-sig")
        count = 0
        for key, zh, _ in ordered:
            for attr in ("Text", "Content", "Header"):
                needle = f'{attr}="{zh}"'
                if needle in raw:
                    n = raw.count(needle)
                    raw = raw.replace(needle, f'{attr}="{{tr:Tr {key}}}"')
                    count += n
        path.write_text(raw, encoding="utf-8")
        total_replaced += count
        print(f"{path.name}: 替换 {count} 处")

    # 报告未被任何文件使用的条目
    unused = []
    for key, zh, _ in table:
        if not any(f"{{tr:Tr {key}}}" in Path(p).read_text(encoding="utf-8-sig") for p in xaml_files):
            unused.append(f"{key} ({zh})")
    if unused:
        print("未命中（可能是动态或已改动）：")
        for u in unused:
            print("  " + u)
    print(f"合计替换 {total_replaced} 处")


if __name__ == "__main__":
    main()
