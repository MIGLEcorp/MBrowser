param(
  [string]$ChromiumSource = 'C:\src\cef154\chromium\src',
  [ValidateRange(1,12)][int]$Jobs = 6
)
$ErrorActionPreference = 'Stop'
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = '0'
$env:GYP_MSVS_OVERRIDE_PATH = 'C:\BuildTools'
$env:GYP_MSVS_VERSION = '2022'
$env:vs2022_install = 'C:\BuildTools'
$pythonDir = Join-Path $ChromiumSource 'third_party\cpython3\host\bin'
if (!(Test-Path (Join-Path $pythonDir 'python3.exe'))) {throw 'Chromium Python3 missing'}
$env:PATH = $pythonDir + ';' + $env:PATH
$out = Join-Path $ChromiumSource 'out\Release_GN_x64'
if (!(Test-Path (Join-Path $out 'build.ninja'))) {throw 'CEF Ninja graph missing'}
if ((Get-PSDrive C).Free -lt 8GB) {throw 'C drive free space too low'}
if ((Get-PSDrive H).Free -lt 25GB) {throw 'H drive free space too low'}
$artifacts = Join-Path $PSScriptRoot '..\artifacts'
$log = Join-Path $artifacts 'cef154-cache-v2-ninja.log'
Set-Location $ChromiumSource
Write-Output "CEF_CACHE_V2_BUILD_JOBS=$Jobs"
Write-Output "CEF_CACHE_V2_LOG=$log"
& ninja.exe -C out\Release_GN_x64 "-j$Jobs" libcef.dll *>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) {throw "CEF native build failed: $LASTEXITCODE"}
$dll = Join-Path $out 'libcef.dll'
if (!(Test-Path $dll)) {throw 'Native libcef.dll was not produced'}
Write-Output "CEF_CACHE_V2_BUILD_SHA256=$((Get-FileHash $dll -Algorithm SHA256).Hash)"
