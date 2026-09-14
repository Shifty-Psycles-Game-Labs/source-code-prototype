#Requires -Version 5.1
param(
    [switch]$Rebuild,
    [ValidateSet('quiet','minimal','normal','detailed','diagnostic')]
    [string]$Verbosity = 'normal',
    [switch]$OpenLog
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$ProjectDir = $PSScriptRoot
$CsprojPath = Join-Path $ProjectDir 'NEMU_Engine.csproj'
$Timestamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$LogPath    = Join-Path $ProjectDir ("nemu_build_" + $Timestamp + ".log")
$LatestLink = Join-Path $ProjectDir 'nemu_build.log'

if (-not (Test-Path $CsprojPath)) {
    Write-Error ("Cannot find " + $CsprojPath + " -- run this script from the project root.")
    exit 1
}

$buildArgs = @('build', $CsprojPath, ("--verbosity:" + $Verbosity), '--nologo')
if ($Rebuild) { $buildArgs += '--no-incremental' }

Write-Host ""
Write-Host ("=== NEMU Build  " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " ===")
Write-Host ("  csproj   : " + $CsprojPath)
Write-Host ("  log      : " + $LogPath)
Write-Host ("  verbosity: " + $Verbosity)
if ($Rebuild) { Write-Host "  mode     : REBUILD (no-incremental)" }
Write-Host ""

$lines    = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$errors   = [System.Collections.Generic.List[string]]::new()

& dotnet @buildArgs 2>&1 | ForEach-Object {
    $raw     = $_.ToString()
    $stamped = ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $raw)
    $lines.Add($stamped)
    Write-Host $stamped
    if ($raw -match ': warning ') { $warnings.Add($raw) }
    if ($raw -match ': error ')   { $errors.Add($raw)   }
}

[System.IO.File]::WriteAllLines($LogPath,    $lines, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllLines($LatestLink, $lines, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "=== Build Summary ==="
Write-Host ("  Warnings : " + $warnings.Count)
Write-Host ("  Errors   : " + $errors.Count)
Write-Host ("  Log      : " + $LogPath)
Write-Host ""

if ($errors.Count -gt 0) {
    Write-Host "--- ERRORS ---"
    $errors | ForEach-Object { Write-Host ('  ' + $_) }
}
if ($warnings.Count -gt 0) {
    Write-Host "--- WARNINGS ---"
    $warnings | ForEach-Object { Write-Host ('  ' + $_) }
}

if ($OpenLog) { Start-Process $LogPath }