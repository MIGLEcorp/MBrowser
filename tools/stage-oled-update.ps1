$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root 'artifacts\MBrowser-4.4.3-portable'
Push-Location $root
try {
    $published = Join-Path $root 'artifacts\MBrowser-4.4.3-published'
    dotnet publish .\MBrowser.csproj -c Release -r win-x64 --self-contained true -p:BuildProjectReferences=false -o $published --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & robocopy (Join-Path $root 'dist') $destination /E /R:1 /W:1 /NFL /NDL /NP /NJH /NJS
    if ($LASTEXITCODE -ge 8) { throw "Failed to stage previous verified CEF runtime: $LASTEXITCODE" }
    foreach ($name in @('MBrowser.exe','MBrowser.dll','MBrowser.deps.json','MBrowser.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $published $name) -Destination (Join-Path $destination $name) -Force
    }
    $runtimeConfig = Get-Content (Join-Path $destination 'MBrowser.runtimeconfig.json') -Raw
    if ($runtimeConfig -notmatch 'includedFrameworks') { throw 'Portable runtime is not self-contained' }
    Copy-Item -LiteralPath (Join-Path $root 'assets\logo.png') -Destination (Join-Path $destination 'assets\logo.png') -Force
    Copy-Item -LiteralPath (Join-Path $root 'assets\mbrowser.ico') -Destination (Join-Path $destination 'assets\mbrowser.ico') -Force
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination -Force
    foreach ($name in @('libcef.dll','resources.pak','v8_context_snapshot.bin')) {
        $sourceHash = (Get-FileHash (Join-Path $root ('dist\' + $name)) -Algorithm SHA256).Hash
        $destHash = (Get-FileHash (Join-Path $destination $name) -Algorithm SHA256).Hash
        if ($sourceHash -ne $destHash) { throw "CEF runtime SHA-256 mismatch: $name" }
    }
    $selftestLog = Join-Path $root 'artifacts\MBrowser-4.4.3-core-tests.txt'
    $selftest = Start-Process -FilePath (Join-Path $destination 'MBrowser.exe') -ArgumentList '--self-test' -Wait -PassThru -RedirectStandardOutput $selftestLog
    if ($selftest.ExitCode -ne 0) { throw "Portable self-test failed: $($selftest.ExitCode)" }
    $smoke = Start-Process -FilePath (Join-Path $destination 'MBrowser.exe') -ArgumentList '--smoke-test' -Wait -PassThru
    if ($smoke.ExitCode -ne 0) { throw "Portable smoke-test failed: $($smoke.ExitCode)" }
    Write-Output 'PORTABLE_TESTS_OK'
    $compiler = Join-Path $root 'bin\tools\InnoSetup\ISCC.exe'
    if (!(Test-Path $compiler)) { throw 'Inno Setup compiler missing' }
    $original = Get-Content (Join-Path $root 'installer\MBrowser.iss') -Raw -Encoding UTF8
    $script = $original.Replace('Source: "..\dist\*"','Source: "..\artifacts\MBrowser-4.4.3-portable\*"')
    $installerPath = Join-Path $root 'installer\MBrowser-4.4.3.iss'
    Set-Content -LiteralPath $installerPath -Value $script -Encoding UTF8
    & $compiler $installerPath
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed' }
    Write-Output ("PORTABLE_OK=" + $destination)
    Write-Output ("INSTALLER_OK=" + (Join-Path $root 'artifacts\MBrowser-4.4.3-Setup-x64.exe'))
} finally {
    Pop-Location
}