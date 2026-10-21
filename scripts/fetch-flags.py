"""把 twemoji 国旗 SVG 转成本地 PNG，供 WPF 显示。

背景：
  Windows 不渲染区域指示符（U+1F1E6..U+1F1FF）组合，界面里写 🇺🇸 只会显示成
  "US" 两个方框字。所以国旗必须用位图。

素材来源：
  twemoji（Twitter Emoji）的旗帜 SVG，许可 CC-BY 4.0，
  再分发需要保留署名 —— 见 docs/THIRD-PARTY-ASSETS.md。
  只取"旗帜"这一个子集，体积极小。

用法：
  python scripts/fetch-flags.py
  可选 --proxy http://127.0.0.1:10808（默认读 HTTPS_PROXY 环境变量）

输出：
  v2rayN/v2rayN/Resources/Flags/{cc}.png   (48x48，透明背景)
"""

import os
import sys
import argparse
import urllib.request

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "..", "v2rayN", "v2rayN", "Resources", "Flags")

BASE = "https://raw.githubusercontent.com/twitter/twemoji/master/assets/svg/"

# 覆盖 IpLocationResult.CountryNamesZh 里的全部国家，
# 保证界面上任何出现过中文名的国家都有旗。
COUNTRIES = [
    "cn", "hk", "mo", "tw", "jp", "kr", "sg", "my", "th", "vn", "id", "ph",
    "in", "pk", "bd", "lk", "kh", "la", "mm",
    "us", "ca", "mx", "br", "ar", "cl", "co",
    "gb", "de", "fr", "it", "es", "ru", "nl", "ch", "se", "no", "dk", "fi",
    "pl", "cz", "at", "gr", "pt", "tr", "ua", "ro", "ie", "be", "hu", "bg",
    "ae", "sa", "il", "kz", "ir",
    "au", "nz", "za", "eg", "ng", "ke",
]

SIZE = 48


def flag_codepoints(cc: str) -> str:
    """两位国家代码 -> twemoji 文件名（区域指示符号的码点）。"""
    return "-".join(f"{0x1F1E6 + ord(ch) - ord('a'):x}" for ch in cc.lower())


def fetch(cc: str, proxy: str | None) -> bytes:
    url = BASE + flag_codepoints(cc) + ".svg"
    req = urllib.request.Request(url, headers={"User-Agent": "P2ray-build"})

    if proxy:
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({"http": proxy, "https": proxy})
        )
        with opener.open(req, timeout=30) as r:
            return r.read()

    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read()


def svg_to_png(svg: bytes, out_path: str) -> None:
    """
    用 resvg-py 渲染。选它而不是 cairosvg：
    cairosvg 依赖系统装了原生 cairo-2.dll，Windows 上默认没有，
    装上也会在 import 阶段报 OSError；resvg-py 是自带绑定（Rust 实现）的 wheel，
    pip 装完就能用，CI 上不会额外失败。
    """
    try:
        import resvg_py
    except ImportError:
        raise SystemExit(
            "需要 resvg-py 来把 SVG 转 PNG：\n"
            "    pip install resvg-py"
        )

    png = resvg_py.svg_to_bytes(svg_string=svg.decode("utf-8"),
                                width=SIZE, height=SIZE)
    with open(out_path, "wb") as f:
        f.write(bytes(png))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--proxy", default=os.environ.get("HTTPS_PROXY") or None,
                        help="HTTP 代理，如 http://127.0.0.1:10808")
    args = parser.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)

    ok, failed = 0, []
    for cc in COUNTRIES:
        out = os.path.join(OUT_DIR, f"{cc}.png")
        if os.path.exists(out) and os.path.getsize(out) > 0:
            ok += 1
            continue
        try:
            svg = fetch(cc, args.proxy)
            svg_to_png(svg, out)
            ok += 1
            print(f"  {cc}  ok")
        except SystemExit:
            raise
        except Exception as e:
            failed.append((cc, str(e)[:80]))
            print(f"  {cc}  FAILED {e}")

    print(f"\n完成 {ok}/{len(COUNTRIES)}，输出目录 {os.path.abspath(OUT_DIR)}")
    if failed:
        print("失败：")
        for cc, msg in failed:
            print(f"  {cc}: {msg}")
        sys.exit(1)


if __name__ == "__main__":
    main()
