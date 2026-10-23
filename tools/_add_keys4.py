import sys
sys.path.insert(0, r'D:\desk\P2ray\P2ray\tools')
from i18n_apply import add_resx_entry, RESX_ZH, RESX_EN
rows = [
 ("P2SetSubCountFmt", "已配置 {0} 个订阅", "{0} subscriptions configured"),
 ("P2SetNoSubYet", "尚未配置订阅", "No subscriptions yet"),
 ("P2SetOff", "关闭", "Off"),
 ("P2SetHoursFmt", "{0} 小时", "{0} h"),
]
zh = RESX_ZH.read_text(encoding='utf-8-sig'); en = RESX_EN.read_text(encoding='utf-8-sig')
for k,z,e in rows:
    zh,_a = add_resx_entry(zh,k,z); en,_b = add_resx_entry(en,k,e)
RESX_ZH.write_text(zh, encoding='utf-8'); RESX_EN.write_text(en, encoding='utf-8')
print("已写入 4 条")
