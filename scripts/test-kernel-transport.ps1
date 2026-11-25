# Compile the EXACT same C++ protocol/ring implementation that the
# WDK kernel prototype also compiles. No kernel driver is loaded/installed.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'vswhere.exe missing' }
$vs = (& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
    Select-Object -First 1)
if (!$vs) { throw 'MSVC x64 toolchain unavailable' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$sourceDir = Join-Path $root 'src\AuralWeave.VirtualDriver\Transport'
$out = Join-Path $root 'build\driver-transport-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out 'AuralWeavePcmWireSmoke.exe'
$lib = '"' + (Join-Path $sourceDir 'aw_pcm_wire.cpp') + '"'
$tests = '"' + (Join-Path $sourceDir 'aw_pcm_wire_smoke.cpp') + '"'
$compile = 'cl /nologo /std:c++20 /O2 /EHsc /W4 /WX /utf-8 ' +
    $lib + ' ' + $tests + ' /Fe:"' + $exe + '"'
Push-Location $out
try {
    & cmd.exe /d /s /c ('call "' + $vcvars + '" >nul && ' + $compile)
    if ($LASTEXITCODE -ne 0) { throw "PCM wire test build failed ($LASTEXITCODE)" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "PCM wire tests failed ($LASTEXITCODE)" }
    Write-Output 'KERNEL_PORTABLE_WIRE_PROTOCOL_TESTS=PASS'
}
finally {
    Pop-Location
}
