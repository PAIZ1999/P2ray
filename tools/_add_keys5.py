import sys
sys.path.insert(0, r'D:\desk\P2ray\P2ray\tools')
from i18n_apply import add_resx_entry, RESX_ZH, RESX_EN
rows = [
 ("P2SetCores", "内核", "Cores"),
 ("P2SetCoreMissing", "未安装", "Not installed"),
 ("P2SetOpenFolder", "打开目录", "Open folder"),
 ("P2SetCheckCoreUpdate", "检查内核更新", "Check core updates"),
 ("P2SetCheckCoreUpdateHint", "从官方 Release 拉取最新内核", "Downloads the latest core from its official release"),
]
zh = RESX_ZH.read_text(encoding='utf-8-sig'); en = RESX_EN.read_text(encoding='utf-8-sig')
for k,z,e in rows:
    zh,_a = add_resx_entry(zh,k,z); en,_b = add_resx_entry(en,k,e)
RESX_ZH.write_text(zh, encoding='utf-8'); RESX_EN.write_text(en, encoding='utf-8')
print("已写入 5 条内核相关文案")
