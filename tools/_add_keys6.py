import sys, io, re
sys.path.insert(0, r'D:\desk\P2ray\P2ray\tools')
from i18n_apply import add_resx_entry, RESX_ZH, RESX_EN
rows = [
 ("P2TrafficConnCount", "连接次数", "Connections"),
 ("P2TrafficConnTotal", "区间内连接次数合计", "Total connections in range"),
 ("P2TrafficDomainHintXray", "Xray 内核不提供 clash API，这里按 access log 统计连接次数（不含字节）。需在设置里打开日志、级别 info。",
                            "Xray has no clash API; this page counts connections from the access log (no byte counts). Logging must be enabled at info level."),
]
zh = RESX_ZH.read_text(encoding='utf-8-sig'); en = RESX_EN.read_text(encoding='utf-8-sig')
for k, z, e in rows:
    zh, _ = add_resx_entry(zh, k, z); en, _ = add_resx_entry(en, k, e)
RESX_ZH.write_text(zh, encoding='utf-8'); RESX_EN.write_text(en, encoding='utf-8')
print("已写入 3 条文案")
