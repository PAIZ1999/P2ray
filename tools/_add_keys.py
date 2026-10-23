import sys, html
sys.path.insert(0, r'D:\desk\P2ray\P2ray\tools')
from i18n_apply import add_resx_entry, RESX_ZH, RESX_EN
rows = [
 ("P2HomeNoServer", "还没有选择线路", "No node selected"),
 ("P2HomeClickToStart", "点下面的按钮开始使用", "Click the button below to start"),
 ("P2HomeSelectNodeHint", "先去「线路」页挑一个，或从剪贴板导入订阅", "Pick one on the Nodes page, or import a subscription"),
 ("P2HomeRoutingHint", "决定哪些网站的流量经过代理。", "Choose which sites go through the proxy."),
 ("P2HomeCurrentIp", "当前出口 IP", "Current exit IP"),
 ("P2HomeNotTested", "尚未检测", "Not tested yet"),
 ("P2HomeSiteStatusFmt", "站点连通性　{0} / {1} 可用", "Site reachability {0} / {1} available"),
]
zh = RESX_ZH.read_text(encoding='utf-8-sig'); en = RESX_EN.read_text(encoding='utf-8-sig')
a=b=0
for k,z,e in rows:
    zh,a1 = add_resx_entry(zh,k,z); en,b1 = add_resx_entry(en,k,e); a+=a1; b+=b1
RESX_ZH.write_text(zh, encoding='utf-8'); RESX_EN.write_text(en, encoding='utf-8')
print(f"resx 追加 zh={a} en={b}")
