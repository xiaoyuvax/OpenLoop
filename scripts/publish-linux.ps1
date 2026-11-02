param([string]$Out = "artifacts/linux-x64")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
$workerDir = Join-Path $outDir 'worker'

Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue

# Layout matches the Windows shell: OpenLoop + worker/OpenLoop.A + worker/OpenLoop.I.
dotnet publish (Join-Path $root 'src/OpenLoop.A') -c Release -r linux-x64 --self-contained true -o $workerDir
if ($LASTEXITCODE -ne 0) { throw "publish A failed ($LASTEXITCODE)" }
dotnet publish (Join-Path $root 'src/OpenLoop.I') -c Release -r linux-x64 --self-contained true -o $workerDir
if ($LASTEXITCODE -ne 0) { throw "publish I failed ($LASTEXITCODE)" }
dotnet publish (Join-Path $root 'src/OpenLoop.Shell') -c Release -r linux-x64 --self-contained true -o $outDir
if ($LASTEXITCODE -ne 0) { throw "publish shell failed ($LASTEXITCODE)" }

Write-Host "linux-x64 bundle: $outDir"
Write-Host "run: ./OpenLoop"
