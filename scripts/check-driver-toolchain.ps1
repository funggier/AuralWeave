param([switch]$Strict)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$kitRoot = 'C:\Program Files (x86)\Windows Kits\10'
$kitVersion = '10.0.28000.0'
$wdkNugetVersion = '10.0.28000.2526'
$nuget = Join-Path $root 'build\nuget-packages\microsoft.windows.wdk.x64\10.0.28000.2526\c'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'

function Show-Part([string]$name, [string]$path) {
    $exists = Test-Path -LiteralPath $path
    $status = if ($exists) { 'FOUND' } else { 'MISSING' }
    Write-Host "$name : $status"
    Write-Host "    $path"
    return $exists
}
$systemPaths = @{
    'System WDK kernel headers' = Join-Path $kitRoot "Include\$kitVersion\km\wdm.h"
    'System PortCls header' = Join-Path $kitRoot "Include\$kitVersion\km\portcls.h"
    'System PortCls library' = Join-Path $kitRoot "Lib\$kitVersion\km\x64\portcls.lib"
    'System ntoskrnl library' = Join-Path $kitRoot "Lib\$kitVersion\km\x64\ntoskrnl.lib"
    'System Inf2Cat tool' = Join-Path $kitRoot "bin\$kitVersion\x86\Inf2Cat.exe"
    'Matching Windows SDK' = Join-Path $kitRoot "Include\$kitVersion\um\Windows.h"
}
$cachePaths = @{
    'NuGet WDK portcls.h' = Join-Path $nuget "Include\$kitVersion\km\portcls.h"
    'NuGet WDK portcls.lib' = Join-Path $nuget "Lib\$kitVersion\km\x64\portcls.lib"
    'NuGet WDK ntoskrnl.lib' = Join-Path $nuget "Lib\$kitVersion\km\x64\ntoskrnl.lib"
    'NuGet WDK Inf2Cat' = Join-Path $nuget "bin\$kitVersion\x86\Inf2Cat.exe"
    'Microsoft SysVAD reference source' = Join-Path $root 'build\sysvad-reference\audio\sysvad\sysvad.sln'
}
$systemOk = $true
foreach ($pair in $systemPaths.GetEnumerator()) {
    $found = Show-Part $pair.Key $pair.Value
    if (!$found) { $systemOk = $false }
}
$cacheOk = $true
foreach ($pair in $cachePaths.GetEnumerator()) {
    $found = Show-Part $pair.Key $pair.Value
    if (!$found) { $cacheOk = $false }
}
$integration = $false
if (Test-Path -LiteralPath $vswhere) {
    $vs = @(& $vswhere -all -products '*' -requires 'Component.Microsoft.Windows.DriverKit' -property installationPath 2>$null)
    $integration = @($vs | Where-Object { $_.Trim() }).Count -gt 0
    Write-Output "Visual Studio WDK integration: $(if($integration){'FOUND'}else{'MISSING'})"
}
Write-Output "System WDK status: $(if($systemOk){'READY'}else{'INCOMPLETE'})"
Write-Output "Project-local WDK cache status: $(if($cacheOk){'READY'}else{'INCOMPLETE'})"
if ($Strict -and ((!$systemOk -and !$cacheOk) -or !$integration)) {
    throw 'Driver development toolchain is incomplete. See missing checks above.'
}
