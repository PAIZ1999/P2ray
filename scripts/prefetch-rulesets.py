"""
预置 sing-box 规则集（.srs）到 bin/srss/，让打包产物无需联网即可启动 sing-box 内核。

为什么需要：
    v2rayN 生成 sing-box 配置时，若 bin/srss/<tag>.srs 不存在，会退化为远程引用
        { "type": "remote", "url": "https://raw.githubusercontent.com/2dust/sing-box-rules/..." }
    而 sing-box 启动时会同步下载这些文件，下载失败即 FATAL 退出。
    对无法直连 GitHub 的用户（中国大陆是主要场景），sing-box 内核因此完全不可用，
    应用只报 "SocksPortCheck Timeout"，用户无从定位。

    ServiceLib 的 SingboxRulesetService 会优先使用 bin/srss/ 下的同名文件
    （见 ConvertGeo2Ruleset 中的 localSrss 判断），因此只要把文件放对位置即可，
    无需修改应用代码。

用法：
    python prefetch-rulesets.py --out <path-to-bin>
    python prefetch-rulesets.py --out <path-to-bin> --tags geosite-cn geoip-cn
"""
import argparse
import base64
import json
import os
import sys
import urllib.request

# Windows 上 Python 的 stdout 默认用 ANSI 代码页（cp1252/cp936），
# 输出中文会抛 UnicodeEncodeError 而中断整个构建。
# GitHub Actions 的 windows-latest 正是这种情况，因此显式切到 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

REPO = "2dust/sing-box-rules"
API = f"https://api.github.com/repos/{REPO}/contents"

BRANCH_BY_PREFIX = {
    "geosite-": "rule-set-geosite",
    "geoip-": "rule-set-geoip",
}

# 默认路由模板引用的规则集；与 ServiceLib 生成的默认配置保持一致。
DEFAULT_TAGS = [
    "geosite-google",
    "geosite-private",
    "geosite-cn",
    "geoip-cn",
]


def fetch_srs(filename: str, branch: str) -> bytes:
    url = f"{API}/{filename}?ref={branch}"
    req = urllib.request.Request(url, headers={"User-Agent": "p2ray"})
    # 构建机常开着系统代理，而 urllib 会读取 HTTP(S)_PROXY / 注册表代理设置。
    # 代理若已停止（或指向尚未启动的内核端口），下载会以 WinError 10061 失败。
    # 这里显式不走代理：构建期拉取规则集不应依赖运行环境中的代理状态。
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=90) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    if payload.get("encoding") != "base64":
        raise ValueError(f"unexpected encoding: {payload.get('encoding')}")
    return base64.b64decode(payload["content"])


def branch_for(tag: str) -> str | None:
    for prefix, br in BRANCH_BY_PREFIX.items():
        if tag.startswith(prefix):
            return br
    return None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True, help="bin 目录（srss 将创建于此）")
    ap.add_argument("--tags", nargs="*", default=DEFAULT_TAGS)
    ap.add_argument("--force", action="store_true", help="已有文件也重新下载")
    args = ap.parse_args()

    srss_dir = os.path.join(args.out, "srss")
    os.makedirs(srss_dir, exist_ok=True)
    print(f"规则集目录: {srss_dir}")

    ok, skipped, failed = 0, 0, 0
    for tag in args.tags:
        branch = branch_for(tag)
        if branch is None:
            print(f"  SKIP {tag}（无法推断分支）")
            skipped += 1
            continue

        dest = os.path.join(srss_dir, f"{tag}.srs")
        if os.path.exists(dest) and not args.force:
            print(f"  CACHED {tag}（{os.path.getsize(dest)/1024:.1f} KB）")
            skipped += 1
            continue

        try:
            data = fetch_srs(f"{tag}.srs", branch)
        except Exception as exc:  # noqa: BLE001
            print(f"  FAIL {tag}: {exc}")
            failed += 1
            continue

        with open(dest, "wb") as f:
            f.write(data)
        print(f"  OK   {tag}（{len(data)/1024:.1f} KB）")
        ok += 1

    total = sum(
        os.path.getsize(os.path.join(srss_dir, f))
        for f in os.listdir(srss_dir)
        if f.endswith(".srs")
    )
    print(f"完成：下载 {ok}，跳过 {skipped}，失败 {failed}，合计 {total/1024:.1f} KB")

    # 失败不是致命错误：应用会退化为远程引用，只是失去离线可用性。
    # 但要在 CI 日志里明显可见。
    if failed:
        print("警告：部分规则集未能预置，该内核将依赖运行时下载")
    return 0


if __name__ == "__main__":
    sys.exit(main())
