"""
把 piflow-logo.svg 渲染成 Windows 需要的多尺寸 .ico 与 PNG。

Logo 是白色单色矢量：外层 P 形（带镂空方块）+ 斜切变换。
不依赖 SVG 渲染库，直接按路径几何重绘，用 4x 超采样保证边缘质量。

图形语义（viewBox 0 0 100 100）：
  M 5 5 H 59 V 59 H 23 V 95 H 5 Z          外框 P
  M 23 23 H 41 V 41 H 23 Z                 左上镂空方块
  M 41 23 H 95 V 41 H 59 V 59 H 77 V 77 H 59 V 95 H 41 Z   右侧折线块
  整体再套 translate(50,50) scale(0.84) skewX(-15) translate(-50,-50)

关于 GLYPH_SHIFT_X：
  skewX(-15) 把字形上边缘向右推、下边缘向左拉，纯几何居中（center=47.97）之后
  视觉重心仍然偏左 —— 底边被推到了 x≈2，而右上的折线块又让它看起来更疏。
  向右平移 3 个 viewBox 单位后左右边距接近（左 5.07 / 右 3.12），
  小尺寸下不再显得"贴左边框"。

关于背景：
  黑底（#111111）而不是品牌蓝，配合白色字形形成最高对比，
  在 Windows 深色任务栏与浅色标题栏上都不刺眼。
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "..", "v2rayN", "v2rayN", "Resources")

# 品牌背景色：黑底
BG = (17, 17, 17)       # #111111
FG = (255, 255, 255)

# 字形相对几何中心的水平右移量（viewBox 单位）
GLYPH_SHIFT_X = 3.0

SUPERSAMPLE = 4
S = 100.0  # viewBox 尺寸


def skew_matrix():
    """返回与 SVG 等价的坐标变换：translate(50,50) scale(.84) skewX(-15) translate(-50,-50)。"""
    import math
    k = math.tan(math.radians(-15))
    sc = 0.84
    cx = cy = 50.0

    def apply(x, y):
        # translate(-50,-50)
        x1, y1 = x - cx, y - cy
        # skewX
        x2 = x1 + k * y1
        y2 = y1
        # scale
        x3, y3 = x2 * sc, y2 * sc
        # translate(50,50) + 视觉右移
        return x3 + cx + GLYPH_SHIFT_X, y3 + cy

    return apply


def build_icon(size: int, with_bg: bool) -> Image.Image:
    """渲染指定尺寸的图标。with_bg=True 时铺黑色圆角底（应用图标）。"""
    px = size * SUPERSAMPLE
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    tr = skew_matrix()

    def poly(points):
        pts = [tr(x, y) for x, y in points]
        pts = [(p[0] / S * px, p[1] / S * px) for p in pts]
        d.polygon(pts, fill=FG + (255,))

    if with_bg:
        # 圆角矩形底，圆角比例约 22%
        r = int(px * 0.22)
        d.rounded_rectangle([0, 0, px - 1, px - 1], radius=r, fill=BG + (255,))

    # 外框 P：M 5 5 H 59 V 59 H 23 V 95 H 5 Z
    poly([(5, 5), (59, 5), (59, 59), (23, 59), (23, 95), (5, 95)])

    # 内部挖空：用透明色覆盖（fill-rule evenodd）
    def hole(points):
        pts = [tr(x, y) for x, y in points]
        pts = [(p[0] / S * px, p[1] / S * px) for p in pts]
        d.polygon(pts, fill=(0, 0, 0, 0) if not with_bg else BG + (255,))

    hole([(23, 23), (41, 23), (41, 41), (23, 41)])

    # 右侧折线块：M 41 23 H 95 V 41 H 59 V 59 H 77 V 77 H 59 V 95 H 41 Z
    poly([(41, 23), (95, 23), (95, 41), (59, 41), (59, 59),
          (77, 59), (77, 77), (59, 77), (59, 95), (41, 95)])

    return img.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]

    # 应用主图标：带品牌底
    icons = [build_icon(s, with_bg=True) for s in sizes]
    ico_path = os.path.join(OUT_DIR, "P2ray.ico")
    icons[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    print(f"wrote {ico_path}")

    # 托盘图标：同样带底，尺寸小一些
    tray = [build_icon(s, with_bg=True) for s in [16, 24, 32, 48]]
    for i in range(1, 5):
        p = os.path.join(OUT_DIR, f"NotifyIcon{i}.ico")
        tray[-1].save(p, format="ICO", sizes=[(s, s) for s in [16, 24, 32, 48]])
        print(f"wrote {p}")

    # 导出 PNG 供 README 与关于页使用
    build_icon(512, with_bg=True).save(os.path.join(HERE, "..", "docs", "logo-512.png"))
    print("wrote docs/logo-512.png")


if __name__ == "__main__":
    main()
