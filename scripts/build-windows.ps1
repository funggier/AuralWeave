param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$RunDeviceDiagnostics,
    [switch]$PublishPortable
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    # Audio input discovery and rendering both require the Native C++ DLL.
    # Never publish a UI that silently falls back to a different backend.
    & (Join-Path $root 'scripts\build-native.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Native build returned a nonzero exit code' }

    $dll = Join-Path $root 'build\native\AuralWeave.Native.dll'
    if (!(Test-Path -LiteralPath $dll)) {
        throw "Required native audio engine missing: $dll"
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

    foreach ($project in @('src\AuralWeave.Desktop', 'src\AuralWeave.Diagnostics')) {
        $target = Join-Path $root ($project + '\bin\' + $Configuration + '\net8.0-windows\AuralWeave.Native.dll')
        Copy-Item -LiteralPath $dll -Destination $target -Force
    }

    & dotnet run --no-build -c $Configuration --project 'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Managed route configuration tests failed' }

    if ($RunDeviceDiagnostics) {
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj'
        if ($LASTEXITCODE -ne 0) { throw 'Native device diagnostics failed' }
    }

    if ($PublishPortable) {
        $target = Join-Path $root 'dist\AuralWeave-win-x64'
        # Clean the target directory so removed dependencies (like NAudio) do
        # not survive in the next portable build.
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }

        & dotnet publish 'src/AuralWeave.Desktop/AuralWeave.Desktop.csproj' `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -o $target --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }

        Copy-Item -LiteralPath $dll -Destination (Join-Path $target 'AuralWeave.Native.dll') -Force
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $target 'LICENSE') -Force
        Copy-Item -LiteralPath (Join-Path $root 'docs\PREVIEW_NOTES.md') -Destination (Join-Path $target 'PREVIEW_NOTES.md') -Force
        Copy-Item -LiteralPath (Join-Path $root 'docs\THIRD_PARTY_NOTICES.md') -Destination (Join-Path $target 'THIRD_PARTY_NOTICES.md') -Force

        if (!(Test-Path -LiteralPath (Join-Path $target 'AuralWeave.exe'))) {
            throw 'Portable publish did not produce AuralWeave.exe'
        }
        # Keep the installed product folder clean; .pdb remains in build/bin.
        Get-ChildItem -LiteralPath $target -File -Filter '*.pdb' |
            Remove-Item -Force

        $csprojXml = [xml](Get-Content -LiteralPath (Join-Path $root 'src\AuralWeave.Desktop\AuralWeave.Desktop.csproj') -Raw)
        $version = [string]$csprojXml.Project.PropertyGroup.Version
        if ($version -notmatch '^\d+\.\d+\.\d+(-[a-z0-9.]+)?$') {
            throw "Invalid application version: $version"
        }
        $versionNotes = Get-Content -LiteralPath (Join-Path $root "docs\releases\v$version.md") -Raw -Encoding UTF8
        $routingGuide = Get-Content -LiteralPath (Join-Path $root 'docs\VB_CABLE.md') -Raw -Encoding UTF8
        $releaseNotes = $versionNotes.TrimEnd() + "`r`n`r`n" + $routingGuide.TrimEnd()
        Set-Content -LiteralPath (Join-Path $target 'RELEASE_NOTES.md') -Value $releaseNotes -Encoding UTF8
        $licenseFolder = Join-Path $target 'licenses'
        New-Item -ItemType Directory -Force -Path $licenseFolder | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'third_party\rnnoise\COPYING') -Destination (Join-Path $licenseFolder 'RNNoise-v0.1.1-BSD-3-Clause.txt') -Force

        Copy-Item -LiteralPath (Join-Path $root 'third_party\signalsmith-stretch\LICENSE.txt') -Destination (Join-Path $licenseFolder 'Signalsmith-Stretch-MIT.txt') -Force
        Copy-Item -LiteralPath (Join-Path $root 'third_party\signalsmith-linear\LICENSE.txt') -Destination (Join-Path $licenseFolder 'Signalsmith-Linear-MIT.txt') -Force

        $archive = Join-Path $root ("dist\AuralWeave-v$version-Windows-x64.zip")
        Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive -CompressionLevel Optimal -Force
        if (!(Test-Path -LiteralPath $archive)) { throw 'Portable ZIP was not created' }
        Write-Output "Native-only portable ZIP: $archive"
        Write-Output "Native-only portable output: $target"
    }
}
finally { Pop-Location }
