# 把源码构建成某个角色的新版本目录，并（可选）让运行中的实例 stage + restart。
# 更新通道另一头（壳的 ApplyUpdate / 回滚）本来就有，这里补上"谁往 versions/ 里写"。
# 用法：.\scripts\publish-role.ps1 -Role a -Version 0.1.1 [-Restart] [-Configuration Release]
param(
    [Parameter(Mandatory = $true)][ValidateSet('a', 'i')][string]$Role,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Configuration = 'Release',
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$data = Join-Path $env:APPDATA 'OpenLoop'
$upper = $Role.ToUpperInvariant()
$exe = "OpenLoop.$upper.exe"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("openloop-publish-$Role-" + [guid]::NewGuid().ToString('N'))

dotnet publish (Join-Path $repo "src\OpenLoop.$upper") -c $Configuration -o $tmp --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish 失败（$Configuration）" }

$built = Join-Path $tmp $exe
if (-not (Test-Path $built)) { throw "产物里没有 $exe" }
$sha = (Get-FileHash $built -Algorithm SHA256).Hash.ToLowerInvariant()
$target = Join-Path $data ("versions\$Role\$Version-" + $sha.Substring(0, 8))
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $tmp '*') $target -Recurse -Force
Remove-Item $tmp -Recurse -Force

Write-Host "已发布 $Role -> $target"

$runtimeFile = Join-Path $data "runtime\$Role.json"
if (-not (Test-Path $runtimeFile)) {
    Write-Host "实例没在跑：只落了目录，下次 stage/restart 再切"
    return
}

$runtime = [IO.File]::ReadAllText($runtimeFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
$key = ([IO.File]::ReadAllText((Join-Path $data 'bus.key'), [Text.Encoding]::UTF8)).Trim()
$base = "http://$($runtime.bus)"
$header = "X-OpenLoop-Key: $key"

$env:OPENLOOP_STAGE = (@{ version = $Version; dir = $target; sha256 = $sha; protocol = $runtime.protocol } | ConvertTo-Json -Compress)
& zcurl POST "$base/bus/stage" -H $header --body-env OPENLOOP_STAGE --timeout 20000
if ($LASTEXITCODE -ne 0) { throw "stage 失败（见上面的响应）" }

if ($Restart) {
    $env:OPENLOOP_RESTART = (@{ version = $Version; reason = 'publish-role' } | ConvertTo-Json -Compress)
    & zcurl POST "$base/bus/restart" -H $header --body-env OPENLOOP_RESTART --timeout 20000
    if ($LASTEXITCODE -ne 0) { throw "restart 被拒（忙就等会话结束再跑一次）" }
    Write-Host "已请求重启：壳正在拉起 $target"
}
