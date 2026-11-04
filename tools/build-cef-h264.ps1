param([ValidateRange(1,12)][int]$Jobs = 4)
$ErrorActionPreference='Stop'
$src='C:\src\cef154\chromium\src'
$repo='H:\zhopa\MBrowser'
$out=Join-Path $src 'out\Release_GN_x64'
$log=Join-Path $repo 'artifacts\cef154-codecs-native-build.log'
$errors=Join-Path $repo 'artifacts\cef154-codecs-native-errors.log'
$argsText=Get-Content (Join-Path $out 'args.gn') -Raw
if($argsText -notmatch '(?m)^proprietary_codecs=true\r?$' -or
   $argsText -notmatch '(?m)^ffmpeg_branding="Chrome"\r?$') {throw 'Codec GN args missing'}
if(!(Test-Path (Join-Path $repo 'artifacts\cef154-before-codecs-libcef.dll'))) {
    throw 'Original native libcef backup missing'
}
if((Get-Item $out).LinkType -ne 'Junction') {throw 'CEF output must be on H drive'}
if((Get-PSDrive C).Free -lt 10GB -or (Get-PSDrive H).Free -lt 40GB) {
    throw 'Not enough disk space'
}
$env:DEPOT_TOOLS_WIN_TOOLCHAIN='0'
$env:GYP_MSVS_OVERRIDE_PATH='C:\BuildTools'
$env:GYP_MSVS_VERSION='2022'
$env:vs2022_install='C:\BuildTools'
$env:PATH=(Join-Path $src 'third_party\cpython3\host\bin')+';'+$env:PATH
$exe=(Get-Command ninja.exe -ErrorAction Stop).Source
Set-Location $src
Write-Output "CODECS_BUILD_JOBS=$Jobs"
Write-Output "CODECS_BUILD_LOG=$log"
$p=Start-Process -FilePath $exe -ArgumentList @('-C','out\Release_GN_x64',"-j$Jobs",'libcef.dll') -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $errors
try {$p.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal} catch {}
Write-Output "CODECS_BUILD_CHILD_PID=$($p.Id)"
$p.WaitForExit()
$p.Refresh()
Write-Output "CODECS_BUILD_EXIT=$($p.ExitCode)"
if($p.ExitCode -ne 0) {
    Get-Content $errors -Tail 25 -ErrorAction SilentlyContinue
    throw 'Native codec build failed; installed browser not touched'
}
Write-Output ('CODECS_CEF_SHA256='+(Get-FileHash (Join-Path $out 'libcef.dll') -Algorithm SHA256).Hash)
