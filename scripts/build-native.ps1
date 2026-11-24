param([switch]$ListDevices)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer/vswhere.exe is unavailable.' }

$vs = (& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
    Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($vs)) { throw 'MSVC C++ toolchain is missing.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (!(Test-Path -LiteralPath $vcvars)) { throw "Missing MSVC environment script: $vcvars" }

$src = Join-Path $root 'src\AuralWeave.Native'
$include = Join-Path $src 'include'
$out = Join-Path $root 'build\native'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$common = '/nologo /std:c++20 /EHsc /W4 /WX /utf-8 /I"' + $include + '" '
$graph = '"' + (Join-Path $src 'src\route_graph.cpp') + '" '
$resampler = '"' + (Join-Path $src 'src\audio_resampler.cpp') + '" '
$catalog = '"' + (Join-Path $src 'src\device_catalog.cpp') + '" '
$tests = '"' + (Join-Path $src 'tests\native_smoke.cpp') + '" '
$engine = '"' + (Join-Path $src 'src\audio_engine.cpp') + '" '
$exe = Join-Path $out 'AuralWeaveNativeSmoke.exe'
$dll = Join-Path $out 'AuralWeave.Native.dll'

$compileTests = 'cl ' + $common + $graph + $resampler + $catalog + $tests +
    '/link ole32.lib propsys.lib /OUT:"' + $exe + '"'
$compileEngine = 'cl ' + $common + '/LD ' + $engine + $resampler +
    '/link ole32.lib uuid.lib /OUT:"' + $dll + '"'

Push-Location $out
try {
    & cmd.exe /d /s /c ('call "' + $vcvars + '" >nul && ' + $compileTests + ' && ' + $compileEngine)
    if ($LASTEXITCODE -ne 0) { throw "Native compile/link failed ($LASTEXITCODE)" }
    if ($ListDevices) { & $exe --devices } else { & $exe }
    if ($LASTEXITCODE -ne 0) { throw "Native smoke test failed ($LASTEXITCODE)" }
    if (!(Test-Path -LiteralPath $dll)) { throw 'Native WASAPI DLL was not generated' }
    Write-Output "Native WASAPI library: $dll"
}
finally { Pop-Location }
