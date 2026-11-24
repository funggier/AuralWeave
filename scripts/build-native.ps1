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
$third = Join-Path $root 'third_party\rnnoise'
$rnSrc = Join-Path $third 'src'
$rnInc = Join-Path $third 'include'
if (!(Test-Path -LiteralPath (Join-Path $third 'COPYING'))) {
    throw 'Pinned RNNoise v0.1.1 source and COPYING file are mandatory; do not auto-download during builds.'
}

$out = Join-Path $root 'build\native'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$rnNames = @('denoise', 'rnn', 'rnn_data', 'rnn_reader', 'pitch', 'kiss_fft', 'celt_lpc')
$rnFiles = ($rnNames | ForEach-Object { '"' + (Join-Path $rnSrc ($_ + '.c')) + '"' }) -join ' '
$rnObjs = ($rnNames | ForEach-Object { '"' + (Join-Path $out ($_ + '.obj')) + '"' }) -join ' '
$compileRnnoise = 'cl /nologo /O2 /TC /D_USE_MATH_DEFINES /I"' + $rnSrc +
    '" /I"' + $rnInc + '" /c ' + $rnFiles

$common = '/nologo /std:c++20 /EHsc /W4 /WX /utf-8 /I"' + $include +
    '" /I"' + $rnInc + '" '
$graph = '"' + (Join-Path $src 'src\route_graph.cpp') + '" '
$resampler = '"' + (Join-Path $src 'src\audio_resampler.cpp') + '" '
$noise = '"' + (Join-Path $src 'src\rnnoise_processor.cpp') + '" '
$filterChain = '"' + (Join-Path $src 'src\filter_chain.cpp') + '" '
$catalog = '"' + (Join-Path $src 'src\device_catalog.cpp') + '" '
$catalogApi = '"' + (Join-Path $src 'src\device_catalog_api.cpp') + '" '
$tests = '"' + (Join-Path $src 'tests\native_smoke.cpp') + '" '
$engine = '"' + (Join-Path $src 'src\audio_engine.cpp') + '" '
$bridge = '"' + (Join-Path $src 'src\virtual_audio_bridge.cpp') + '" '
$bridgeApi = '"' + (Join-Path $src 'src\virtual_audio_bridge_api.cpp') + '" '
$bridgeTest = '"' + (Join-Path $src 'tests\virtual_bridge_smoke.cpp') + '" '
$exe = Join-Path $out 'AuralWeaveNativeSmoke.exe'
$dll = Join-Path $out 'AuralWeave.Native.dll'
$bridgeExe = Join-Path $out 'AuralWeaveVirtualBridgeSmoke.exe'

$compileTests = 'cl ' + $common + $graph + $resampler + $noise + $filterChain + $catalog +
    $tests + $rnObjs + ' /link ole32.lib propsys.lib /OUT:"' + $exe + '"'
$compileBridge = 'cl ' + $common + $bridge + $bridgeApi + $bridgeTest + ' /link ole32.lib /OUT:"' + $bridgeExe + '"'
$compileEngine = 'cl ' + $common + '/LD ' + $bridge + $bridgeApi + $engine + $resampler + $noise + $filterChain +
    $catalog + $catalogApi + $rnObjs + ' /link ole32.lib propsys.lib uuid.lib /OUT:"' + $dll + '"'

Push-Location $out
try {
    & cmd.exe /d /s /c ('call "' + $vcvars + '" >nul && ' +
        $compileRnnoise + ' && ' + $compileTests + ' && ' + $compileBridge + ' && ' + $compileEngine)
    if ($LASTEXITCODE -ne 0) { throw "Native compile/link failed ($LASTEXITCODE)" }
    & $bridgeExe
    if ($LASTEXITCODE -ne 0) { throw "Virtual PCM bridge cross-process test failed ($LASTEXITCODE)" }
    if ($ListDevices) { & $exe --devices } else { & $exe }
    if ($LASTEXITCODE -ne 0) { throw "Native smoke test failed ($LASTEXITCODE)" }
    if (!(Test-Path -LiteralPath $dll)) { throw 'Native WASAPI DLL was not generated' }
    Write-Output "Native WASAPI + RNNoise library: $dll"
}
finally { Pop-Location }
