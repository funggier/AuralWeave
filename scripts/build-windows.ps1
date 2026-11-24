param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$RunDeviceDiagnostics,
    [switch]$PublishPortable,
    [switch]$SkipNative
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    if (!$SkipNative) {
        & (Join-Path $root 'scripts\build-native.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Native build returned non-zero exit code' }
    }
    $projects = @(
        'src/AuralWeave.Core/AuralWeave.Core.csproj',
        'src/AuralWeave.Desktop/AuralWeave.Desktop.csproj',
        'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj',
        'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    )
    foreach ($project in $projects) {
        & dotnet build $project -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    $dll = Join-Path $root 'build\native\AuralWeave.Native.dll'
    if (!$SkipNative -and (Test-Path -LiteralPath $dll)) {
        foreach ($target in @('src\AuralWeave.Desktop', 'src\AuralWeave.Diagnostics')) {
            Copy-Item -LiteralPath $dll -Destination (Join-Path $root ($target + '\bin\' + $Configuration + '\net8.0-windows\AuralWeave.Native.dll')) -Force
        }
    }
    & dotnet run --no-build -c $Configuration --project 'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    if ($RunDeviceDiagnostics) {
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj'
        if ($LASTEXITCODE -ne 0) { throw 'Device diagnostics failed' }
    }
    if ($PublishPortable) {
        $target = Join-Path $root 'dist/AuralWeave-win-x64'
        & dotnet publish 'src/AuralWeave.Desktop/AuralWeave.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $target --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
        if (!$SkipNative -and (Test-Path -LiteralPath $dll)) {
            Copy-Item -LiteralPath $dll -Destination (Join-Path $target 'AuralWeave.Native.dll') -Force
        }
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $target 'LICENSE') -Force
        Copy-Item -LiteralPath (Join-Path $root 'docs\PREVIEW_NOTES.md') -Destination (Join-Path $target 'PREVIEW_NOTES.md') -Force
        Copy-Item -LiteralPath (Join-Path $root 'docs\THIRD_PARTY_NOTICES.md') -Destination (Join-Path $target 'THIRD_PARTY_NOTICES.md') -Force
        $licenseDir = Join-Path $target 'licenses'
        New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'third_party\licenses\NAudio-2.2.1-LICENSE.txt') -Destination (Join-Path $licenseDir 'NAudio-2.2.1-LICENSE.txt') -Force
        Write-Output "Portable output directory: $target"
    }
}
finally { Pop-Location }
