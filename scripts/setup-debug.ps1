#requires -Version 7.0
<#
.SYNOPSIS
    为本地 Debug 构建补齐运行所需的代理内核与 Geo 文件。

.DESCRIPTION
    为什么需要这个脚本：
      bin/ 目录被 .gitignore 排除，内核（xray.exe / sing-box.exe）和
      geoip.dat / geosite.dat 都不进仓库；而 fetch-cores.ps1 原先只在
      build-release.ps1 里被调用。于是全新 clone 之后直接 F5 调试，
      内核根本不存在，Xray 启动即失败 —— 界面却仍然显示"已连接"，
      排查起来非常费时。

    这个脚本把 Debug 输出目录一次补齐，之后正常调试即可。
    发布包不受影响：build-release.ps1 自己会拉内核。

.EXAMPLE
    pwsh ./scripts/setup-debug.ps1
    pwsh ./scripts/setup-debug.ps1 -Runtime win-x64 -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # 重新下载内核，忽略 .core-cache
    [switch]$Force,

    # 跳过规则集预置（只影响 sing-box 的 srs 文件）
    [switch]$SkipRulesets
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$MainProj = Join-Path $RepoRoot 'v2rayN\v2rayN\v2rayN.csproj'

# ---------------------------------------------------------------------------
# 1. 定位 SDK（与 build-release.ps1 同一套候选顺序）
# ---------------------------------------------------------------------------
$candidates = @()
if ($env:DOTNET_ROOT) {
    $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
}
$onPath = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if ($onPath) {
    $candidates += $onPath
}

$dotnet = $null
foreach ($candidate in $candidates) {
    if (-not (Test-Path $candidate)) { continue }
    $ver = & $candidate --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $ver) {
        $dotnet = $candidate
        break
    }
}

if (-not $dotnet) {
    throw "未找到可用的 .NET SDK。请安装 SDK，或设置 DOTNET_ROOT。已尝试：$($candidates -join ', ')"
}

Write-Host "仓库根目录 : $RepoRoot" -ForegroundColor Cyan
Write-Host "SDK        : $dotnet" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# 2. 先构建一次 Debug，确保输出目录存在
#    不构建直接猜路径，会在 RID / TFM 变化时失效。
# ---------------------------------------------------------------------------
Write-Host "[1/3] 构建 Debug 以确定输出目录" -ForegroundColor Green
& $dotnet build $MainProj -c Debug -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "构建失败 ($LASTEXITCODE)" }

# 用程序集名定位实际输出目录，避免把 TFM 写死在脚本里
$exe = Get-ChildItem -Path (Join-Path $RepoRoot 'v2rayN\v2rayN\bin\Debug') -Recurse -Filter 'P2ray.exe' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $exe) {
    throw "未找到 Debug 输出目录下的 P2ray.exe"
}

$OutDir = $exe.DirectoryName
$CoreDir = Join-Path $OutDir 'bin'
Write-Host "输出目录   : $OutDir" -ForegroundColor Cyan
Write-Host "内核目录   : $CoreDir" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# 3. 拉内核 + Geo 文件
# ---------------------------------------------------------------------------
Write-Host "[2/3] 拉取代理内核与 Geo 文件" -ForegroundColor Green
$fetchArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'fetch-cores.ps1'), '-OutputDir', $CoreDir)
if ($Force) { $fetchArgs += '-Force' }
& pwsh @fetchArgs
if ($LASTEXITCODE -ne 0) { throw "内核拉取失败 ($LASTEXITCODE)" }

# ---------------------------------------------------------------------------
# 4. 预置 sing-box 规则集（与发布包行为一致）
# ---------------------------------------------------------------------------
Write-Host ""
if ($SkipRulesets) {
    Write-Host "[3/3] 跳过规则集预置 (-SkipRulesets)" -ForegroundColor Yellow
} else {
    Write-Host "[3/3] 预置 sing-box 规则集" -ForegroundColor Green
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & python (Join-Path $PSScriptRoot 'prefetch-rulesets.py') --out $CoreDir
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  警告：规则集预置失败，sing-box 将依赖运行时下载" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  警告：未找到 python，跳过规则集预置" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# 5. 校验
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "校验产物：" -ForegroundColor Green
$required = @(
    (Join-Path $CoreDir 'xray\xray.exe'),
    (Join-Path $CoreDir 'geosite.dat'),
    (Join-Path $CoreDir 'geoip.dat')
)
$missing = @()
foreach ($path in $required) {
    if (Test-Path $path) {
        Write-Host ("  OK   {0} ({1:N1} MB)" -f ($path.Replace($OutDir + '\', ''), ((Get-Item $path).Length / 1MB))) -ForegroundColor DarkGray
    } else {
        $missing += $path
        Write-Host ("  MISS {0}" -f $path.Replace($OutDir + '\', '')) -ForegroundColor Red
    }
}

if ($missing.Count -gt 0) {
    throw "缺少 $($missing.Count) 个必需文件，Debug 构建无法启动内核"
}

Write-Host ""
Write-Host "完成。现在可以正常 F5 调试。" -ForegroundColor Green
