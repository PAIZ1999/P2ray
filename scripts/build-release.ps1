#requires -Version 7.0
<#
.SYNOPSIS
    构建可分发 P2ray Windows 发布包。

.DESCRIPTION
    流程：
      1. dotnet publish 主程序（自包含、单文件）
      2. dotnet publish 自更新辅助工具
      3. 拉取并放置 Xray-core / sing-box 内核到 bin/
      4. 清理调试符号，输出到 dist/

    产物结构（与 ServiceLib 运行时约定一致）：
      dist/P2ray-win-x64/
        P2ray.exe
        bin/xray/xray.exe
        bin/sing_box/sing-box.exe
        <其余托管依赖>

.EXAMPLE
    pwsh ./scripts/build-release.ps1 -Runtime win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # 跳过内核拉取（内核已在缓存中时加速本地迭代）
    [switch]$SkipCores,

    # 跳过自包含发布，产物更小但要求目标机已装 .NET 运行时
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SlnDir = Join-Path $RepoRoot 'v2rayN'
$MainProj = Join-Path $SlnDir 'v2rayN\v2rayN.csproj'
$ToolProj = Join-Path $SlnDir 'AmazTool\AmazTool.csproj'

$DistRoot = Join-Path $RepoRoot 'dist'
$OutDir = Join-Path $DistRoot "P2ray-$Runtime"

Write-Host "仓库根目录 : $RepoRoot"
Write-Host "运行时     : $Runtime"
Write-Host "输出目录   : $OutDir"
Write-Host ""

# ---------------------------------------------------------------------------
# 0. 前置检查
# ---------------------------------------------------------------------------
# 候选顺序：DOTNET_ROOT（便携式 SDK）→ PATH 里的 dotnet。
# 逐个用 --version 验证，只有真的能报出版本号的才算可用 ——
# 本机可能装着仅运行时、没有 SDK 的 dotnet.exe，那种会直接失败。
$candidates = @()
if ($env:DOTNET_ROOT) {
    $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
}
$onPath = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if ($onPath) {
    $candidates += $onPath
}

$dotnet = $null
$sdkVersion = $null
foreach ($candidate in $candidates) {
    if (-not (Test-Path $candidate)) { continue }
    $ver = & $candidate --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $ver) {
        $dotnet = $candidate
        $sdkVersion = $ver
        break
    }
}

if (-not $dotnet) {
    throw "未找到可用的 .NET 10 SDK。请安装 SDK，或设置 DOTNET_ROOT 指向 SDK 根目录。已尝试：$($candidates -join ', ')"
}

# 后续调用统一走 $dotnet，避免 PATH 与 DOTNET_ROOT 不一致时找不到
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
Write-Host "dotnet SDK : $sdkVersion ($dotnet)" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $OutDir) {
    Write-Host "清理旧产物..." -ForegroundColor DarkGray
    Remove-Item $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# ---------------------------------------------------------------------------
# 1. 发布主程序
# ---------------------------------------------------------------------------
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

Write-Host "[1/4] 发布 P2ray 主程序" -ForegroundColor Green
$mainArgs = @(
    'publish', $MainProj,
    '-c', 'Release',
    '-r', $Runtime,
    "-p:SelfContained=$selfContained",
    '-p:PublishSingleFile=false',
    '-o', $OutDir
)
& $dotnet @mainArgs
if ($LASTEXITCODE -ne 0) { throw "主程序发布失败 ($LASTEXITCODE)" }

# ---------------------------------------------------------------------------
# 2. 发布自更新辅助工具
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/4] 发布自更新辅助工具" -ForegroundColor Green
$toolArgs = @(
    'publish', $ToolProj,
    '-c', 'Release',
    '-r', $Runtime,
    "-p:SelfContained=$selfContained",
    '-p:PublishSingleFile=true',
    '-o', $OutDir
)
& $dotnet @toolArgs
if ($LASTEXITCODE -ne 0) { throw "辅助工具发布失败 ($LASTEXITCODE)" }

# ---------------------------------------------------------------------------
# 3. 内核
# ---------------------------------------------------------------------------
Write-Host ""
if ($SkipCores) {
    Write-Host "[3/4] 跳过内核拉取 (-SkipCores)" -ForegroundColor Yellow
} else {
    Write-Host "[3/4] 拉取代理内核" -ForegroundColor Green
    $coreOut = Join-Path $OutDir 'bin'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'fetch-cores.ps1') -OutputDir $coreOut
    if ($LASTEXITCODE -ne 0) { throw "内核拉取失败 ($LASTEXITCODE)" }

    # 预置 sing-box 规则集到 bin/srss/。
    # 若不预置，ServiceLib 会生成指向 raw.githubusercontent.com 的远程引用，
    # sing-box 启动时同步下载，失败即退出——对无法直连 GitHub 的用户等于内核不可用。
    # ServiceLib 会优先使用 bin/srss/<tag>.srs（见 SingboxRulesetService.ConvertGeo2Ruleset）。
    Write-Host "  预置 sing-box 规则集..." -ForegroundColor DarkGray
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & python (Join-Path $PSScriptRoot 'prefetch-rulesets.py') --out $coreOut
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  警告：规则集预置失败，sing-box 内核将依赖运行时下载" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  警告：未找到 python，跳过规则集预置" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# 4. 清理
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/4] 清理调试符号与多余文件" -ForegroundColor Green
Get-ChildItem $OutDir -Recurse -File -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $OutDir -Recurse -File -Include *.xml | Where-Object { $_.Name -match '\.(deps|runtimeconfig)\.' } | Remove-Item -Force -ErrorAction SilentlyContinue

# 记录产物清单，便于核对
$manifest = Get-ChildItem $OutDir -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject]@{
            Path   = $_.FullName.Replace($OutDir, '').TrimStart('\')
            Size   = $_.Length
            Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    }
$manifestPath = Join-Path $OutDir 'SHA256SUMS.txt'
$manifest | ForEach-Object { "$($_.Sha256)  $($_.Path)" } | Set-Content -Path $manifestPath -Encoding utf8

$totalMB = [math]::Round((($manifest | Measure-Object Size -Sum).Sum) / 1MB, 1)
Write-Host ""
Write-Host "完成。文件数 $($manifest.Count)，合计 $totalMB MB" -ForegroundColor Green
Write-Host "产物: $OutDir" -ForegroundColor Cyan
Write-Host "校验: $manifestPath" -ForegroundColor Cyan
