"""
把 sing-box 配置里的远程 rule_set 改写为本地文件引用。

背景：
    v2rayN 生成的 sing-box 配置默认引用 4 个远程规则集：
        route.rule_set[i] = { "type": "remote", "url": "https://raw.githubusercontent.com/..." }
    sing-box 启动时会同步下载这些文件，下载失败则 FATAL 退出。
    对无法直连 GitHub 的用户，sing-box 内核因此完全不可用，
    而应用只报 "SocksPortCheck Timeout"，用户无法自行定位。

本脚本：
    1. 从 GitHub API 下载同源 .srs 文件（绕开 raw.githubusercontent.com 的不可达问题）
    2. 把配置中的 remote rule_set 改成 local，并指向同目录下的文件名

用法：
    python localize-rulesets.py <config.json> <output-dir>
"""
import base64
import json
import os
import sys
import urllib.request

# Windows 上 Python 的 stdout 默认是 ANSI 代码页（cp1252/cp936），
# 输出中文会抛 UnicodeEncodeError。CI 与本地控制台都会遇到，统一转 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

REPO = "2dust/sing-box-rules"
API = f"https://api.github.com/repos/{REPO}/contents"

# 分支名与 sing-box 配置里的 URL 路径段一致
BRANCH_BY_PREFIX = {
    "geosite-": "rule-set-geosite",
    "geoip-": "rule-set-geoip",
}


def fetch_srs(filename: str, branch: str) -> bytes:
    url = f"{API}/{filename}?ref={branch}"
    req = urllib.request.Request(url, headers={"User-Agent": "p2ray"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    return base64.b64decode(payload["content"])


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    config_path, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)

    with open(config_path, encoding="utf-8") as f:
        config = json.load(f)

    rule_sets = config.get("route", {}).get("rule_set", [])
    if not rule_sets:
        print("config has no route.rule_set, nothing to do")
        return 0

    downloaded = 0
    for rs in rule_sets:
        if rs.get("type") != "remote":
            continue

        url = rs.get("url", "")
        filename = url.rsplit("/", 1)[-1]
        if not filename.endswith(".srs"):
            print(f"skip (not .srs): {url}")
            continue

        branch = None
        for prefix, br in BRANCH_BY_PREFIX.items():
            if filename.startswith(prefix):
                branch = br
                break
        if branch is None:
            print(f"skip (unknown prefix): {filename}")
            continue

        try:
            data = fetch_srs(filename, branch)
        except Exception as exc:  # noqa: BLE001
            print(f"FAIL download {filename}: {exc}")
            return 1

        with open(os.path.join(out_dir, filename), "wb") as f:
            f.write(data)
        downloaded += 1

        # 改为本地引用
        rs["type"] = "local"
        rs["path"] = filename
        rs.pop("url", None)
        # local 类型不接受 http_client 字段，sing-box 会 FATAL 拒绝
        rs.pop("http_client", None)
        print(f"localized {filename} ({len(data)/1024:.1f} KB)")

    # 规则集全部本地化后，下载用的 http_client 不再被引用
    remaining_remote = [rs for rs in rule_sets if rs.get("type") == "remote"]
    if not remaining_remote and "http_clients" in config:
        config.pop("http_clients")
        print("removed unused http_clients")

    with open(config_path, "w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False, indent=2)

    print(f"done: {downloaded} rule-set(s) localized")
    return 0


if __name__ == "__main__":
    sys.exit(main())
