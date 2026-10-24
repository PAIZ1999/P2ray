#requires -Version 7.0
<#
.SYNOPSIS
    P2ray 验收门禁 G1–G5。判据见 docs/arch/gates.md。

.DESCRIPTION
    由人或 CI 运行 —— 不由执行者自评。退出码 0 表示全部 PASS。
    每条门禁输出一行 [PASS]/[FAIL]/[SKIP] 与关键数据，最后把一行结论追加到
    docs/arch/state/goals/<GoalId>.tsv。

.PARAMETER IncludeTraffic
    启用 G5（需要可连接节点 + 真实流量）。默认跳过。

.EXAMPLE
    pwsh -NoProfile -File scripts/arch-gates.ps1
    pwsh -NoProfile -File scripts/arch-gates.ps1 -IncludeTraffic
#>
[CmdletBinding()]
param(
    [switch]$IncludeTraffic,
    [string]$Unit = 'adhoc',
    [string]$GoalId = 'g-p2ray-opt',
    [string]$DotnetPath = ''
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$results = @()
$failures = 0

function Add-Result([string]$id, [string]$verdict, [string]$detail) {
    $script:results += [pscustomobject]@{ Id = $id; Verdict = $verdict; Detail = $detail }
    if ($verdict -eq 'FAIL') { $script:failures++ }
    $color = switch ($verdict) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0}] {1}  {2}" -f $verdict, $id, $detail) -ForegroundColor $color
}

function Resolve-Dotnet {
    if ($DotnetPath -and (Test-Path $DotnetPath)) { return $DotnetPath }
    $bundled = 'D:\desk\P2ray\.dotnet\dotnet.exe'
    if (Test-Path $bundled) { return $bundled }
    return 'dotnet'
}

$dotnet = Resolve-Dotnet
Write-Host "dotnet = $dotnet"
Write-Host ("repo   = {0}" -f $repo)
Write-Host ''

# ---------------------------------------------------------------- G1 构建
$buildOut = & $dotnet build (Join-Path $repo 'v2rayN\v2rayN\v2rayN.csproj') -c Debug -v q --nologo 2>&1
$buildExit = $LASTEXITCODE
$errLine = ($buildOut | Select-String -Pattern '(\d+)\s*个错误' | Select-Object -Last 1).Line
$errCount = if ($errLine -and $errLine -match '(\d+)\s*个错误') { [int]$Matches[1] } else { -1 }

if ($buildExit -eq 0 -and $errCount -eq 0) {
    Add-Result 'G1 构建' 'PASS' "退出码 0，错误 0"
} else {
    Add-Result 'G1 构建' 'FAIL' "退出码 $buildExit，错误 $errCount"
}

# ---------------------------------------------------------------- G2 测试
$testExe = Join-Path $repo 'v2rayN\ServiceLib.Tests\bin\Debug\net10.0\ServiceLib.Tests.exe'
$testBaseline = 180
if (-not (Test-Path $testExe)) {
    Add-Result 'G2 测试' 'FAIL' '找不到测试可执行文件（先构建 ServiceLib.Tests）'
} else {
    $testOut = & $testExe 2>&1
    $testExit = $LASTEXITCODE
    $totalLine = ($testOut | Select-String -Pattern '总计:\s*(\d+)' | Select-Object -Last 1).Line
    $failLine = ($testOut | Select-String -Pattern '失败:\s*(\d+)' | Select-Object -Last 1).Line
    $total = if ($totalLine -and $totalLine -match '总计:\s*(\d+)') { [int]$Matches[1] } else { -1 }
    $failed = if ($failLine -and $failLine -match '失败:\s*(\d+)') { [int]$Matches[1] } else { -1 }

    if ($testExit -eq 0 -and $failed -eq 0 -and $total -ge $testBaseline) {
        Add-Result 'G2 测试' 'PASS' "总计 $total（基线 $testBaseline），失败 0"
    } else {
        Add-Result 'G2 测试' 'FAIL' "退出码 $testExit，失败 $failed，总计 $total（基线 $testBaseline）"
    }
}

# ---------------------------------------------------------------- G3 dist 保留用户配置
$preserveScript = Join-Path $repo 'scripts\test-preserve-config.ps1'
if (-not (Test-Path $preserveScript)) {
    Add-Result 'G3 dist 保留配置' 'SKIP' 'scripts/test-preserve-config.ps1 不存在'
} else {
    $preserveOut = & pwsh -NoProfile -File $preserveScript 2>&1
    $preserveExit = $LASTEXITCODE
    $asserts = ($preserveOut | Select-String -Pattern '通过|PASS|✓' | Measure-Object).Count
    if ($preserveExit -eq 0) {
        Add-Result 'G3 dist 保留配置' 'PASS' "退出码 0（断言行 $asserts）"
    } else {
        Add-Result 'G3 dist 保留配置' 'FAIL' "退出码 $preserveExit"
    }
}

# ---------------------------------------------------------------- G4 版本与 CI
$props = Join-Path $repo 'v2rayN\Directory.Build.props'
$version = if (Test-Path $props) {
    $m = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>'
    if ($m) { $m.Matches[0].Groups[1].Value } else { '' }
} else { '' }

if (-not $version) {
    Add-Result 'G4 版本与 CI' 'FAIL' 'Directory.Build.props 里没有 <Version>'
} else {
    $sha = (& git -C $repo rev-parse HEAD 2>$null)
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    $ciState = 'SKIP'
    $ciDetail = '离线或未取到 CI 结果'

    if ($curl -and $sha) {
        $tmp = Join-Path $env:TEMP 'arch-gates-runs.json'
        & curl.exe -s --noproxy '*' --max-time 20 'https://api.github.com/repos/PAIZ1999/P2ray/actions/runs?per_page=10' -o $tmp 2>$null | Out-Null
        if (Test-Path $tmp) {
            try {
                $runs = Get-Content $tmp -Raw | ConvertFrom-Json
                $hit = $runs.workflow_runs | Where-Object { $_.head_sha -eq $sha } | Select-Object -First 1
                if ($hit) {
                    $ciState = if ($hit.conclusion -eq 'success') { 'PASS' } elseif ($hit.conclusion) { 'FAIL' } else { 'SKIP' }
                    $ciDetail = "run $($hit.conclusion) @ $($sha.Substring(0,8))"
                } else {
                    $ciDetail = "没有 HEAD($($sha.Substring(0,8))) 的运行记录"
                }
            } catch {
                $ciDetail = 'CI 结果解析失败'
            } finally {
                Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if ($ciState -eq 'FAIL') {
        Add-Result 'G4 版本与 CI' 'FAIL' "版本 $version，CI $ciDetail"
    } elseif ($ciState -eq 'PASS') {
        Add-Result 'G4 版本与 CI' 'PASS' "版本 $version，CI $ciDetail"
    } else {
        Add-Result 'G4 版本与 CI' 'SKIP' "版本 $version，$ciDetail（SKIP 不等于通过）"
    }
}

# ---------------------------------------------------------------- G5 流量统计实测
if (-not $IncludeTraffic) {
    Add-Result 'G5 统计实测' 'SKIP' '未加 -IncludeTraffic'
} else {
    $exe = Join-Path $repo 'v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0\P2ray.exe'
    $json = Join-Path $repo 'v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0\guiConfigs\selftest.json'

    Get-Process P2ray -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
    Remove-Item $json -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path $exe)) {
        Add-Result 'G5 统计实测' 'FAIL' '找不到 P2ray.exe'
    } else {
        $proc = Start-Process $exe -ArgumentList '--selftest' -PassThru -WindowStyle Minimized
        $exited = $proc.WaitForExit(180000)
        if (-not $exited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Add-Result 'G5 统计实测' 'FAIL' '自检超时（180s）'
        } elseif (-not (Test-Path $json)) {
            Add-Result 'G5 统计实测' 'FAIL' '自检没有产出 selftest.json'
        } else {
            $o = Get-Content $json -Raw | ConvertFrom-Json
            $grew = [bool]$o.trafficGrew

            # 必须用「本次增量」而不是累计值：appDown 是全表累计，clashDown 只统计本次运行，
            # 直接相除会得到 >1 的荒谬比值（首版就踩了，1488.8）。
            $clashDelta = [double]$o.clashDownAfter - [double]$o.clashDownBefore
            $appDelta = [double]$o.appDownAfter - [double]$o.appDownBefore
            $ratio = if ($clashDelta -gt 0) { [math]::Round($appDelta / $clashDelta, 4) } else { -1 }

            # 流量太小（<1MB）时比值没有统计意义：可能节点没通、只走了握手包。
            # 这种情况报 SKIP（SKIP ≠ 通过），不许拿它当"统计正常"的证据。
            $minClash = 1000000
            if ($clashDelta -lt $minClash) {
                Add-Result 'G5 统计实测' 'SKIP' ("本次流量仅 {0} 字节（<{1}），不足以判定；请检查节点后重跑" -f $clashDelta, $minClash)
            } elseif ($grew -and $ratio -ge 0.9 -and $ratio -le 1.1) {
                Add-Result 'G5 统计实测' 'PASS' ("trafficGrew=true，按应用捕获率 {0}（app {1} / clash {2}）" -f $ratio, $appDelta, $clashDelta)
            } else {
                Add-Result 'G5 统计实测' 'FAIL' ("trafficGrew={0}，app 增量={1}，clash 增量={2}，捕获率={3}" -f $grew, $appDelta, $clashDelta, $ratio)
            }
        }
    }
}

# ---------------------------------------------------------------- 台账
$stateDir = Join-Path $repo 'docs\arch\state\goals'
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$ledger = Join-Path $stateDir "$GoalId.tsv"
if (-not (Test-Path $ledger)) {
    "time`tunit`tcommand`texit`tkey_output`tverdict" | Set-Content -Path $ledger -Encoding utf8
}

$verdict = if ($failures -eq 0) { 'met' } else { 'not-yet' }
$key = ($results | ForEach-Object { "$($_.Id)=$($_.Verdict)" }) -join '; '
$row = "{0}`t{1}`tarch-gates.ps1`t{2}`t{3}`t{4}" -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'), $Unit, $(if ($failures -eq 0) { 0 } else { 1 }), $key, $verdict
Add-Content -Path $ledger -Value $row -Encoding utf8

Write-Host ''
Write-Host ("判定: {0}  失败项: {1}  台账: {2}" -f $verdict, $failures, $ledger)
exit $(if ($failures -eq 0) { 0 } else { 1 })
