# Native cross-process transport tests always run. Physical audio is
# captured only when the caller explicitly opts in with -WithLiveAudio.
# -WithVirtualOutput uses generated PCM and only a MUTED physical output.
param([switch]$WithLiveAudio, [switch]$WithVirtualOutput)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'build-native.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Native PCM bridge smoke test failed.' }

    if ($WithLiveAudio -or $WithVirtualOutput) {
        & dotnet build 'src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj' -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed diagnostics build failed.' }
        $dll = Join-Path $root 'build\native\AuralWeave.Native.dll'
        $destination = Join-Path $root 'src\AuralWeave.Diagnostics\bin\Release\net8.0-windows\AuralWeave.Native.dll'
        Copy-Item -LiteralPath $dll -Destination $destination -Force
        if ($WithLiveAudio) {
            & dotnet run --no-build -c Release --project 'src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj' -- --smoke-virtual-bridge
            if ($LASTEXITCODE -ne 0) { throw 'Live muted virtual-input tap test failed.' }
        }
        if ($WithVirtualOutput) {
            & dotnet run --no-build -c Release --project 'src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj' -- --smoke-virtual-output
            if ($LASTEXITCODE -ne 0) { throw 'Virtual Output user-mode DSP route test failed.' }
        }
    }

    Write-Output 'VIRTUAL_PCM_BRIDGE_QA=PASS'
    if (!$WithLiveAudio -and !$WithVirtualOutput) {
        Write-Output 'Device tests skipped (use -WithLiveAudio or -WithVirtualOutput to opt in).'
    }
}
finally { Pop-Location }
