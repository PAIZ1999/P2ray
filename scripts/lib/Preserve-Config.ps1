<#
.SYNOPSIS
  在重建 dist 前暂存用户配置，构建后还原。

.DESCRIPTION
  build-release.ps1 会整目录删除产物目录，而 guiConfigs 里是用户的节点、
  订阅与流量历史。把 dist 当作运行目录时（开发机常见），一次重建等于清空配置。

  逻辑单独成库，是为了能脱离"跑一遍完整构建"来验证 —— 完整构建要几分钟，
  不适合放到 CI 每次跑。
#>

function Save-P2rayUserConfig {
    param([Parameter(Mandatory)] [string] $OutDir)

    $configDir = Join-Path $OutDir 'guiConfigs'
    if (-not (Test-Path $configDir)) {
        return $null
    }

    $stash = Join-Path ([System.IO.Path]::GetTempPath()) ("p2ray-guiConfigs-" + [Guid]::NewGuid().ToString('N'))
    Copy-Item $configDir $stash -Recurse -Force
    return $stash
}

function Restore-P2rayUserConfig {
    param(
        [Parameter(Mandatory)] [string] $OutDir,
        [string] $Stash
    )

    if ([string]::IsNullOrWhiteSpace($Stash) -or -not (Test-Path $Stash)) {
        return $false
    }

    $target = Join-Path $OutDir 'guiConfigs'
    if (-not (Test-Path $target)) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
    }

    Copy-Item (Join-Path $Stash '*') $target -Recurse -Force
    Remove-Item $Stash -Recurse -Force -ErrorAction SilentlyContinue
    return $true
}
