"""分析并给出配色层次建议。"""


def lum(h):
    h = h.lstrip('#')
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

    def f(c):
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)


def contrast(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


print("=== 当前色阶 ===")
for k, v in {
    "bg      ": "#0A0A0A",
    "surface ": "#171717",
    "elevated": "#262626",
}.items():
    print(f"  {k} {v}  L={lum(v):.4f}")

print()
print(f"  card/bg 亮度差: {lum('#171717') - lum('#0A0A0A'):.4f}   (过小，卡片浮不起来)")
print(f"  hover/card 差:  {lum('#262626') - lum('#171717'):.4f}")

print()
print("=== 建议色阶 ===")
new = {
    "bg      ": "#111114",
    "surface ": "#1A1A1F",
    "raised  ": "#232329",
    "hover   ": "#2A2A31",
    "border  ": "#34343D",
}
for k, v in new.items():
    print(f"  {k} {v}  L={lum(v):.4f}")

print()
print(f"  card/bg 亮度差: {lum('#1A1A1F') - lum('#111114'):.4f}")
print(f"  border/card 差: {lum('#34343D') - lum('#1A1A1F'):.4f}")

print()
print("=== 断开按钮配色候选（在 #1A1A1F 卡片上）===")
for name, fg, bg, desc in [
    ("A 描边灰", "#E5E7EB", "transparent", "中性，跟随卡片，最不抢眼"),
    ("B 危险红", "#EF4444", "transparent", "语义直白：断开=危险操作"),
    ("C 实心浅灰", "#0F0F12", "#E5E7EB", "反色对比最强，像'停止'按钮"),
]:
    if bg == "transparent":
        c = contrast(fg, "#1A1A1F")
    else:
        c = contrast(fg, bg)
    print(f"  {name:12s} fg={fg} bg={bg}  对比 {c:.2f}:1  {desc}")
