param([switch]$ListDevices)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer/vswhere is required.' }
$vs = (& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($vs)) { throw 'MSVC C++ workload is missing.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (!(Test-Path -LiteralPath $vcvars)) { throw "Cannot find $vcvars" }
$out = Join-Path $root 'build\native'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Push-Location $out
try {
    $src = Join-Path $root 'src\AuralWeave.Native'
    $include = Join-Path $src 'include'
    $inputs = @(
        (Join-Path $src 'src\route_graph.cpp'),
        (Join-Path $src 'src\device_catalog.cpp'),
        (Join-Path $src 'tests\native_smoke.cpp')
    )
    $arguments = @('/nologo','/std:c++20','/EHsc','/W4','/WX','/utf-8',('/I"'+$include+'"') ) + ($inputs | ForEach-Object { '"' + $_ + '"' }) + @('/link','ole32.lib','propsys.lib',('/OUT:"'+(Join-Path $out 'AuralWeaveNativeSmoke.exe')+'"'))
    $build = 'call "' + $vcvars + '" >nul && cl ' + ($arguments -join ' ')
    & cmd.exe /d /s /c $build
    if ($LASTEXITCODE -ne 0) { throw "Native compile/link failed ($LASTEXITCODE)" }
    $binary = Join-Path $out 'AuralWeaveNativeSmoke.exe'
    if ($ListDevices) { & $binary --devices } else { & $binary }
    if ($LASTEXITCODE -ne 0) { throw "Native test failed ($LASTEXITCODE)" }
}
finally { Pop-Location }
