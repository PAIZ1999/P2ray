import sys
sys.path.insert(0, r'D:\desk\P2ray\P2ray\tools')
from i18n_apply import add_resx_entry, RESX_ZH, RESX_EN
rows = [
 ("P2TrafficRange3", "3 天", "3 days"),
 ("P2TrafficRange7", "7 天", "7 days"),
 ("P2TrafficRange30", "30 天", "30 days"),
]
zh = RESX_ZH.read_text(encoding='utf-8-sig'); en = RESX_EN.read_text(encoding='utf-8-sig')
for k,z,e in rows:
    zh,_a = add_resx_entry(zh,k,z); en,_b = add_resx_entry(en,k,e)
RESX_ZH.write_text(zh, encoding='utf-8'); RESX_EN.write_text(en, encoding='utf-8')
print("已写入 3 条区间文案")
