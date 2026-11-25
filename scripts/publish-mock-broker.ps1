param(
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    # A separate developer-only user-mode Mock; NEVER a signed kernel Driver.
    if (!$SkipNativeBuild) {
        & (Join-Path $root 'scripts\build-native.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Native PCM bridge build failed' }
    }
    $native = Join-Path $root 'build\native\AuralWeave.Native.dll'
    if (!(Test-Path -LiteralPath $native)) {
        throw "AuralWeave.Native.dll is missing: $native"
    }

    $dest = Join-Path $root 'dist\AuralWeave-MockBroker-win-x64'
    if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Recurse -Force
    }
    & dotnet publish 'src\AuralWeave.Broker\AuralWeave.Broker.csproj' `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $dest --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Mock broker publish failed' }

    Copy-Item -LiteralPath $native -Destination (Join-Path $dest 'AuralWeave.Native.dll') -Force
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $dest 'LICENSE') -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs\THIRD_PARTY_NOTICES.md') -Destination (Join-Path $dest 'THIRD_PARTY_NOTICES.md') -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs\LOCAL_BROKER_MOCK.md') -Destination (Join-Path $dest 'MOCK_BROKER_README.md') -Force

    $start = @'
@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0Cache\DotNetBundle" mkdir "%~dp0Cache\DotNetBundle"
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%~dp0Cache\DotNetBundle"
echo AuralWeave MOCK BROKER - USER MODE ONLY
echo This is NOT a Windows virtual audio driver or service.
echo Press Ctrl+C to stop. No Administrator rights required.
"%~dp0AuralWeave.Broker.exe" --mock
echo.
echo Mock Broker process stopped.
pause
'@
    Set-Content -LiteralPath (Join-Path $dest 'Start-MockBroker.cmd') -Value $start -Encoding ASCII

    Get-ChildItem -LiteralPath $dest -File -Filter '*.pdb' | Remove-Item -Force
    $entries = @(Get-ChildItem -LiteralPath $dest -File)
    foreach ($required in @('AuralWeave.Broker.exe', 'AuralWeave.Native.dll',
            'Start-MockBroker.cmd', 'MOCK_BROKER_README.md', 'LICENSE')) {
        if (!($entries.Name -contains $required)) {
            throw "Missing Mock Broker bundle file: $required"
        }
    }
    $forbidden = @($entries | Where-Object {
        $_.Name -match '(?i)\.(sys|inf|cat|pdb)$' -or
        $_.Name -match '(?i)(VirtualAudioSetup|AuralWeave\.exe)'
    })
    if ($forbidden.Count) { throw 'Forbidden unsigned driver or Studio files in Mock Broker bundle.' }

    $archive = Join-Path $root 'dist\AuralWeave-MockBroker-v0.1.0-Windows-x64.zip'
    Compress-Archive -Path (Join-Path $dest '*') -DestinationPath $archive -Force -CompressionLevel Optimal
    if (!(Test-Path -LiteralPath $archive)) { throw 'Mock Broker ZIP creation failed' }
    Write-Output "MOCK_BROKER_PORTABLE_NO_KERNEL=PASS: $archive"
}
finally {
    Pop-Location
}
