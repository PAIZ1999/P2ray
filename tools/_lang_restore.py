import json
p = r'D:\desk\P2ray\P2ray\v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0\guiConfigs\p2rayConfig.json'
d = json.load(open(p, encoding='utf-8-sig'))
d.setdefault('UiItem', {})['CurrentLanguage'] = 'zh-Hans'
json.dump(d, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
print('语言已恢复 zh-Hans')
