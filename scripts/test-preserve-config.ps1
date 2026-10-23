<#
.SYNOPSIS
  验证「重建 dist 时保留用户配置」这段逻辑。

.DESCRIPTION
  不跑完整构建（那要几分钟），只对一个临时目录模拟"删除产物目录"的过程：
  写入哨兵文件 → 暂存 → 删目录 → 还原 → 断言哨兵仍在。

  这段逻辑曾经缺失，导致一次重建把用户的节点、订阅与流量历史全部清空。
#>

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\Preserve-Config.ps1')

$failures = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if ($Condition) {
        Write-Host "  PASS  $Message" -ForegroundColor Green
    } else {
        Write-Host "  FAIL  $Message" -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "== 用例 1: 有配置时重建应保留 ==" -ForegroundColor Cyan
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("p2ray-preserve-test-" + [Guid]::NewGuid().ToString('N'))
$configDir = Join-Path $root 'guiConfigs'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
Set-Content -Path (Join-Path $configDir 'p2rayDB.db') -Value 'fake-db' -NoNewline
Set-Content -Path (Join-Path $configDir 'SENTINEL.txt') -Value 'keep-me' -NoNewline

$stash = Save-P2rayUserConfig -OutDir $root
Assert-True ([bool]$stash) '应产生暂存目录'

# 模拟 build-release.ps1 的整目录删除
Remove-Item $root -Recurse -Force
Assert-True (-not (Test-Path $root)) '产物目录已删除'

New-Item -ItemType Directory -Path $root -Force | Out-Null
$restored = Restore-P2rayUserConfig -OutDir $root -Stash $stash
Assert-True $restored '还原应成功'
Assert-True (Test-Path (Join-Path $root 'guiConfigs/SENTINEL.txt')) '哨兵文件应仍在'
Assert-True (Test-Path (Join-Path $root 'guiConfigs/p2rayDB.db')) '数据库应仍在'
Assert-True (-not (Test-Path $stash)) '暂存目录应被清理'

Write-Host "== 用例 2: 没有配置时不应出错 ==" -ForegroundColor Cyan
$root2 = Join-Path ([System.IO.Path]::GetTempPath()) ("p2ray-preserve-empty-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root2 -Force | Out-Null
$stash2 = Save-P2rayUserConfig -OutDir $root2
Assert-True ($null -eq $stash2) '无 guiConfigs 时不应产生暂存'
$restored2 = Restore-P2rayUserConfig -OutDir $root2 -Stash $stash2
Assert-True (-not $restored2) '无暂存时还原应返回 false'

Remove-Item $root,$root2 -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    Write-Host "`n$failures 项失败" -ForegroundColor Red
    exit 1
}

Write-Host "`n全部通过" -ForegroundColor Green
exit 0
