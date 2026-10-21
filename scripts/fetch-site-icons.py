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
               "chatgpt.webp", "claude.webp", "taobao.webp", "weixin.webp"}


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

    total = sum(os.path.getsize(os.path.join(OUT_DIR, f))
                for f in os.listdir(OUT_DIR) if f.endswith(".png"))
    print(f"\n{ok}/{len(SLUGS)} saved, total {total / 1024:.1f} KB -> {OUT_DIR}")
    if fail:
        print("failed:", ", ".join(fail))


if __name__ == "__main__":
    main()
