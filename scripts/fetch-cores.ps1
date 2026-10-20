#requires -Version 7.0
<#
.SYNOPSIS
    获取 P2ray 运行所需的代理内核（Xray-core、sing-box），组装到 bin/<coretype>/ 目录。

.DESCRIPTION
    内核直接来自各上游官方 Release，不经过任何中转仓库：
      - Xray-core  : XTLS/Xray-core
      - sing-box   : SagerNet/sing-box
    版本通过下方 $CoreSpec 钉扎，保证构建可复现。

    产物布局遵循 ServiceLib 的约定：
      <OutputDir>\xray\xray.exe
      <OutputDir>\sing_box\sing-box.exe
    CoreInfoManager 会按 <StartupPath>\bin\<CoreType>\<exe> 查找。

.EXAMPLE
    pwsh ./scripts/fetch-cores.ps1 -OutputDir ./v2rayN/v2rayN/bin/Release/net10.0-windows10.0.19041.0/bin
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDir,

    # 仅拉取指定内核，可选：xray / sing_box
    [string[]]$Only,

    # 强制重新下载，忽略已有缓存
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# 内核版本钉扎
# 升级时改这里，并在 PR 里记录上游 changelog 与校验和变化。
# ---------------------------------------------------------------------------
$CoreSpec = @(
    @{
        Name    = 'xray'
        Repo    = 'XTLS/Xray-core'
        Version = 'v26.3.27'
        # Release 资产名
        Asset   = 'Xray-windows-64.zip'
        Exe     = 'xray.exe'
        # 打包后所在子目录（对应 ECoreType 小写）
        SubDir  = 'xray'
    },
    @{
        Name    = 'sing_box'
        Repo    = 'SagerNet/sing-box'
        Version = 'v1.14.3'
        # sing-box 资产名内含去掉 v 前缀的版本号
        Asset   = 'sing-box-{0}-windows-amd64.zip'
        Exe     = 'sing-box.exe'
        SubDir  = 'sing_box'
    }
)

$CacheDir = Join-Path $PSScriptRoot '..' '.core-cache'
$CacheDir = [System.IO.Path]::GetFullPath($CacheDir)

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host "内核输出目录: $OutputDir" -ForegroundColor Cyan
Write-Host "下载缓存目录: $CacheDir" -ForegroundColor DarkGray

if (-not (Test-Path $CacheDir)) {
    New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null
}

function Get-CoreArchive {
    param([hashtable]$Spec)

    $versionForAsset = $Spec.Version.TrimStart('v')
    $assetName = $Spec.Asset
    if ($assetName.Contains('{0}')) {
        $assetName = $assetName -f $versionForAsset
    }

    $url = "https://github.com/$($Spec.Repo)/releases/download/$($Spec.Version)/$assetName"
    $dest = Join-Path $CacheDir $assetName

    if ((Test-Path $dest) -and -not $Force) {
        Write-Host "  使用缓存: $assetName" -ForegroundColor DarkGray
        return $dest
    }

    Write-Host "  下载: $url" -ForegroundColor Yellow
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $dest -TimeoutSec 300 -UseBasicParsing
    return $dest
}

function Expand-CoreSingle {
    param(
        [hashtable]$Spec,
        [string]$Archive,
        [string]$TargetDir
    )

    # 上游 zip 内是 <name>/<exe> 结构，也可能是平铺，两种都处理
    $tmp = Join-Path $CacheDir ("_expand_" + $Spec.Name)
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    Expand-Archive -Path $Archive -DestinationPath $tmp -Force

    $exeSrc = Get-ChildItem -Path $tmp -Recurse -Filter $Spec.Exe | Select-Object -First 1
    if (-not $exeSrc) {
        throw "在 $Archive 中未找到 $($Spec.Exe)"
    }

    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Copy-Item $exeSrc.FullName -Destination (Join-Path $TargetDir $Spec.Exe) -Force

    # geo 数据必须放在 bin/ 根目录，而不是 bin/xray/。
    # 原因：ServiceLib 给 Xray 设置的环境变量是
    #   XRAY_LOCATION_ASSET = <StartupPath>/bin      （见 CoreInfoManager 的 Xray Environment）
    # 若放在子目录，Xray 找不到 geoip.dat/geosite.dat，会在启动后立即退出，
    # 而应用只报 '运行 Core 失败' 这种含糊错误，排查成本极高。
    if ($Spec.Name -eq 'xray') {
        foreach ($geo in @('geoip.dat', 'geosite.dat')) {
            $geoSrc = Get-ChildItem -Path $tmp -Recurse -Filter $geo | Select-Object -First 1
            if ($geoSrc) {
                $dest = Join-Path (Split-Path $TargetDir -Parent) $geo
                Copy-Item $geoSrc.FullName -Destination $dest -Force
                Write-Host "     geo: $dest" -ForegroundColor DarkGray
            }
        }
    }

    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$selected = $CoreSpec
if ($Only) {
    $selected = $CoreSpec | Where-Object { $Only -contains $_.Name }
    if (-not $selected) {
        throw "Only 参数无匹配内核，可选值: $(($CoreSpec | ForEach-Object { $_.Name }) -join ', ')"
    }
}

foreach ($spec in $selected) {
    Write-Host ""
    Write-Host "[$($spec.Name)] $($spec.Repo) $($spec.Version)" -ForegroundColor Green

    $archive = Get-CoreArchive -Spec $spec
    $targetDir = Join-Path $OutputDir $spec.SubDir
    Expand-CoreSingle -Spec $spec -Archive $archive -TargetDir $targetDir

    $exePath = Join-Path $targetDir $spec.Exe
    $size = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    $hash = (Get-FileHash $exePath -Algorithm SHA256).Hash
    Write-Host "  -> $exePath ($size MB)" -ForegroundColor Gray
    Write-Host "     sha256 $hash" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "内核准备完成。" -ForegroundColor Green
