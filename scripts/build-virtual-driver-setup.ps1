# Build future elevated Windows Virtual Driver Setup.
# UNSIGNED developer artifact only; never placed in dist.
# This script only invokes --check, and never changes Windows PnP.
param([switch]$PublishStandalone)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    $project = 'src\AuralWeave.DriverSetup\AuralWeave.DriverSetup.csproj'
    & dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Driver Setup compile failed.' }

    $exe = Join-Path $root 'src\AuralWeave.DriverSetup\bin\Release\net8.0-windows\AuralWeaveVirtualAudioSetup.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Driver Setup EXE missing.' }

    $before = @(Get-PnpDevice -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match '^ROOT\\AURALWEAVE_V(INPUT|OUTPUT)_' } |
        Select-Object -ExpandProperty InstanceId | Sort-Object)
    & $exe --check
    if ($LASTEXITCODE -ne 0) { throw 'Read-only setup preflight failed.' }
    $after = @(Get-PnpDevice -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match '^ROOT\\AURALWEAVE_V(INPUT|OUTPUT)_' } |
        Select-Object -ExpandProperty InstanceId | Sort-Object)
    if (@(Compare-Object -ReferenceObject $before -DifferenceObject $after).Count -gt 0) {
        throw 'Setup --check unexpectedly changed Windows PnP inventory!'
    }
    Write-Output 'DRIVER_SETUP_READ_ONLY_PNP_UNCHANGED=PASS'

    if ($PublishStandalone) {
        $out = Join-Path $root 'build\unsigned-driver-setup-developer-only'
        & dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Developer-only standalone setup publish failed.' }
        $standalone = Join-Path $out 'AuralWeaveVirtualAudioSetup.exe'
        if (!(Test-Path -LiteralPath $standalone)) { throw 'Standalone setup EXE missing.' }
        & $standalone --check
        if ($LASTEXITCODE -ne 0) { throw 'Standalone setup preflight failed.' }
        Write-Output "UNSIGNED_SETUP_DEVELOPER_ONLY=$standalone"
    }
    Write-Output 'VIRTUAL_DRIVER_SETUP_HELPER_BUILD=PASS'
    Write-Warning 'Unsigned developer-only helper. No approved publisher, no Windows driver installed, and no release package.'
}
finally { Pop-Location }
