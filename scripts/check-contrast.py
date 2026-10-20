def lum(hexcolor):
    h = hexcolor.lstrip('#')
    if len(h) == 8:
        h = h[2:]
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

    def f(c):
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)


def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


# 两个背景：卡片 #171717，主背景 #0A0A0A
cans = [
    ("#FAFAFA", "primary text"),
    ("#A3A3A3", "secondary text"),
    ("#737373", "tertiary text"),
    ("#525252", "disabled text"),
    ("#818CF8", "accent text"),
    ("#22C55E", "success"),
]

print(f"{'color':<10}{'on card':>9}{'on bg':>8}  {'name':<16}grade")
for c, name in cans:
    r1 = ratio(c, "#171717")
    r2 = ratio(c, "#0A0A0A")
    if r1 >= 7:
        grade = "AAA"
    elif r1 >= 4.5:
        grade = "AA"
    elif r1 >= 3:
        grade = "AA-large only"
    else:
        grade = "FAIL"
    print(f"{c:<10}{r1:>9.2f}{r2:>8.2f}  {name:<16}{grade}")

print()
print("--- proposed replacements ---")
for old, new, name in [
    ("#737373", "#8B8B8B", "tertiary text"),
    ("#525252", "#666666", "disabled text"),
]:
    print(f"{name}: {old} ({ratio(old, '#171717'):.2f}) -> {new} ({ratio(new, '#171717'):.2f})")
