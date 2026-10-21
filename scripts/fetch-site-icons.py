"""
从 ip.net.coffee 的 /favicons/ 抓站点图标，转成 P2ray 内置资源。

图标必须在本地：连通性面板是用户断网时最需要看的界面之一，
如果图标也要联网取，恰恰在最该显示的时候全是破图。

输出：
  v2rayN/v2rayN/Resources/Sites/<slug>.png   站点图标，24x24

用法：
  python scripts/fetch-site-icons.py --proxy socks5://127.0.0.1:10808
"""
import argparse
import io
import os
import sys
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "v2rayN", "v2rayN", "Resources", "Sites")

# 素材分两处放，不能一律拼同一个前缀（实测）：
#   /favicons/link/xxx.ico   —— 常规 .ico
#   /favicons/xxx            —— 页面里直接引用的 .webp / .png
ICON_BASE_LINK = "https://ip.net.coffee/favicons/link/"
ICON_BASE_ROOT = "https://ip.net.coffee/favicons/"
SIZE = 24

# 需要哪些图标 —— 必须与 NetProbeService.Targets 的 slug 一一对应。
# 值是对端文件名：对方不全是 .ico，混着 .png / .webp，
# 直接拼 slug.ico 会 404。
#
# 只放实际用到的：多抓的图标会被 <Resource Include="Resources\Sites\*.png" />
# 打进程序集，白白增大体积。
SLUGS = {
    "google": "google.ico",
    "youtube": "youtube.webp",
    "github": "github.webp",
    "cloudflare": "cloudflare.webp",
    "openai": "chatgpt.webp",
    "claude": "claude.webp",
    "baidu": "baidu.ico",
    "taobao": "taobao.webp",
    "weixin": "weixin.webp",
}

# 上面这些里哪些在 /favicons/ 根目录
ROOT_HOSTED = {"youtube.webp", "github.webp", "cloudflare.webp",
               "chatgpt.webp", "claude.webp",
               "taobao.webp", "weixin.webp"}

# ip.net.coffee 上没有 gemini 的图标（所有候选路径都 404），
# 改从 Google 自己的 CDN 取官方星形标志。这个 SVG 带径向渐变，
# 交给 Pillow 直接读不了，得先按路径自己填充 —— 见 render_svg_star()。
SVG_SOURCES = {
    "gemini": "https://www.gstatic.com/lamda/images/gemini_sparkle_v002_d4735304ff6292a690345.svg",
}


def icon_url(filename: str) -> str:
    base = ICON_BASE_ROOT if filename in ROOT_HOSTED else ICON_BASE_LINK
    return f"{base}{filename}"


def build_opener(proxy: str | None):
    if not proxy:
        return urllib.request.build_opener()
    handler = urllib.request.ProxyHandler({"http": proxy, "https": proxy})
    return urllib.request.build_opener(handler)


def fetch(opener, url: str) -> bytes:
    req = urllib.request.Request(url, headers={
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                      "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
    })
    with opener.open(req, timeout=25) as resp:
        return resp.read()


def convert(raw: bytes) -> bytes:
    """缩放到 SIZE 并统一转 PNG。ICO / WEBP / JPEG 都交给 Pillow 处理。"""
    from PIL import Image

    img = Image.open(io.BytesIO(raw))
    # ICO 里可能有多帧，取最大的一帧再缩，避免拿到 16x16 糊图
    if getattr(img, "n_frames", 1) > 1:
        best = None
        for i in range(img.n_frames):
            img.seek(i)
            if best is None or img.size[0] > best.size[0]:
                best = img.copy()
        img = best
    img = img.convert("RGBA")
    img.thumbnail((SIZE, SIZE), Image.LANCZOS)
    out = io.BytesIO()
    img.save(out, format="PNG", optimize=True)
    return out.getvalue()


def render_svg_star(svg_text: str, size: int = SIZE) -> bytes:
    """
    渲染 Gemini 的四角星 SVG。

    为什么不用 resvg-py：那只是 fetch-flags.py 的可选依赖，这个脚本不该为
    一个图标多装一个包。而 Pillow 自己又不能解析 SVG。
    好在这个图标的结构极简 —— 一条闭合的 M/C 三次贝塞尔路径 + 一个径向渐变，
    直接采样贝塞尔、再用超采样多边形 + 线性渐变近似填充，24px 下与原始 SVG
    肉眼看不出差别。
    """
    import math
    import re

    from PIL import Image, ImageDraw

    d_attr = re.search(r'd="([^"]+)"', svg_text).group(1)
    vb = re.search(r'viewBox="0 0 ([\d.]+) ([\d.]+)"', svg_text)
    vb_w, vb_h = (float(vb.group(1)), float(vb.group(2))) if vb else (28.0, 28.0)

    # 逐段采样：M 起点，C 用三次贝塞尔公式展开成折线
    tokens = re.findall(r'[MC]|-?\d*\.?\d+', d_attr)
    pts: list[tuple[float, float]] = []
    cur = (0.0, 0.0)
    i = 0
    while i < len(tokens):
        t = tokens[i]
        if t == "M":
            cur = (float(tokens[i + 1]), float(tokens[i + 2]))
            pts.append(cur)
            i += 3
        elif t == "C":
            c1 = (float(tokens[i + 1]), float(tokens[i + 2]))
            c2 = (float(tokens[i + 3]), float(tokens[i + 4]))
            p2 = (float(tokens[i + 5]), float(tokens[i + 6]))
            for k in range(1, 25):
                u = k / 24.0
                x = ((1 - u) ** 3 * cur[0] + 3 * (1 - u) ** 2 * u * c1[0]
                     + 3 * (1 - u) * u * u * c2[0] + u ** 3 * p2[0])
                y = ((1 - u) ** 3 * cur[1] + 3 * (1 - u) ** 2 * u * c1[1]
                     + 3 * (1 - u) * u * u * c2[1] + u ** 3 * p2[1])
                pts.append((x, y))
            cur = p2
            i += 7
        else:
            i += 1

    # 8 倍超采样，缩回 24px 时边缘才不会锯齿
    ss = size * 8
    sx, sy = ss / vb_w, ss / vb_h
    mask = Image.new("L", (ss, ss), 0)
    ImageDraw.Draw(mask).polygon([(x * sx, y * sy) for x, y in pts], fill=255)

    grad = Image.new("RGBA", (ss, ss))
    px = grad.load()
    for y in range(ss):
        for x in range(ss):
            t = x / ss * 0.5 + y / ss * 0.5
            if t < 0.5:
                k = t * 2
                r = int(145 + (86 - 145) * k)
                g = int(104 + (132 - 104) * k)
                b = int(192 + (209 - 192) * k)
            else:
                k = (t - 0.5) * 2
                r = int(86 + (27 - 86) * k)
                g = int(132 + (161 - 132) * k)
                b = int(209 + (227 - 209) * k)
            px[x, y] = (r, g, b, 255)

    out = Image.composite(grad, Image.new("RGBA", (ss, ss), (0, 0, 0, 0)), mask)
    out = out.resize((size, size), Image.LANCZOS)
    buf = io.BytesIO()
    out.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--proxy", default=None, help="如 socks5://127.0.0.1:10808")
    args = ap.parse_args()

    opener = build_opener(args.proxy)
    os.makedirs(OUT_DIR, exist_ok=True)

    ok, fail = 0, []
    for slug, filename in sorted(SLUGS.items()):
        url = icon_url(filename)
        try:
            raw = fetch(opener, url)
            png = convert(raw)
            with open(os.path.join(OUT_DIR, f"{slug}.png"), "wb") as f:
                f.write(png)
            ok += 1
            print(f"  ok   {slug:14} {len(png):6} bytes")
        except Exception as ex:  # noqa: BLE001 - 单个失败不影响其余
            fail.append(f"{slug}({filename})")
            print(f"  FAIL {slug:14} {ex}")

    for slug, url in sorted(SVG_SOURCES.items()):
        try:
            png = render_svg_star(fetch(opener, url).decode("utf-8"))
            with open(os.path.join(OUT_DIR, f"{slug}.png"), "wb") as f:
                f.write(png)
            ok += 1
            print(f"  ok   {slug:14} {len(png):6} bytes  (svg)")
        except Exception as ex:  # noqa: BLE001
            fail.append(f"{slug}({url})")
            print(f"  FAIL {slug:14} {ex}")

    total_slugs = len(SLUGS) + len(SVG_SOURCES)
    total = sum(os.path.getsize(os.path.join(OUT_DIR, f))
                for f in os.listdir(OUT_DIR) if f.endswith(".png"))
    print(f"\n{ok}/{total_slugs} saved, total {total / 1024:.1f} KB -> {OUT_DIR}")
    if fail:
        print("failed:", ", ".join(fail))


if __name__ == "__main__":
    main()
