param(
    [string]$App = (Join-Path $PSScriptRoot '..\dist\MBrowser.exe'),
    [switch]$Legacy
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path -LiteralPath $App).Path
$env:MBROWSER_LEGACY_CEF_SUBPROCESS = if ($Legacy) { '1' } else { '0' }
$before = @(Get-ChildItem $env:TEMP -Directory -Filter 'MBrowser-cef-smoke-*' | Select-Object -ExpandProperty FullName)
$p = Start-Process -FilePath $app -ArgumentList '--cef-smoke' -PassThru
try {
    # Initialization can take several seconds, especially when Windows scans
    # the native CEF DLL. Check repeatedly instead of sampling too early.
    $children = @()
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Seconds 1
        # Enumerate names in PowerShell, not via a full-name WMI filter.
        $children = @(Get-CimInstance Win32_Process |
            Where-Object { $_.ParentProcessId -eq $p.Id -and $_.CommandLine -match '--type=' })
        if ($children.Count -ge 3) { break }
    }
    $matching = if ($Legacy) { 'CefSharp.BrowserSubprocess.exe' } else { 'MBrowser.exe' }
    $valid = @($children | Where-Object {
        $_.Name -eq $matching -and
        $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith([IO.Path]::GetDirectoryName($app),
            [StringComparison]::OrdinalIgnoreCase)
    })
    Write-Output ('MAIN_PID=' + $p.Id)
    Write-Output ('CHILD_PROCESSES=' + $children.Count)
    Write-Output ('MATCHING_PROCESS_IDENTITY=' + $valid.Count)
    foreach ($child in $children) {
        $type = if ($child.CommandLine -match '--type=([^ ]+)') { $Matches[1] } else { 'unknown' }
        Write-Output ('PID=' + $child.ProcessId + ' NAME=' + $child.Name + ' TYPE=' + $type)
    }
    if ($valid.Count -lt 2 -or $children.Count -ne $valid.Count) {
        throw 'CEF subprocess identity does not match expected executable'
    }
    if (!$p.WaitForExit(24000)) { throw 'CEF smoke timeout' }
    $p.Refresh()
    Write-Output ('SMOKE_EXIT=' + $p.ExitCode)
    if ($p.ExitCode -ne 0) { throw 'CEF smoke failed' }
    $dir = Get-ChildItem $env:TEMP -Directory -Filter 'MBrowser-cef-smoke-*' |
        Where-Object FullName -notin $before |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $report = Get-Content (Join-Path $dir.FullName 'cef-smoke.txt')
    if ($report -notcontains 'CEF_RENDERER_INITIALIZED=True') {
        throw 'CEF renderer was not initialized'
    }
    Write-Output 'SUBPROCESS_IDENTITY_SMOKE_OK'
}
finally {
    if (!$p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
