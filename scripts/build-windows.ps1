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
        'src/AuralWeave.Broker/AuralWeave.Broker.csproj',
        'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj',
        'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    )
    foreach ($project in $projects) {
        & dotnet build $project -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }

    foreach ($project in @('src\AuralWeave.Desktop', 'src\AuralWeave.Diagnostics', 'src\AuralWeave.Broker')) {
        $target = Join-Path $root ($project + '\bin\' + $Configuration + '\net8.0-windows\AuralWeave.Native.dll')
        Copy-Item -LiteralPath $dll -Destination $target -Force
    }

    & dotnet run --no-build -c $Configuration --project 'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Managed route configuration tests failed' }

    if ($RunDeviceDiagnostics) {
        # The kernel interface is not installed; verify the managed packet
        # codec and exact PCM16/PCM32 sample conversions against a deterministic
        # test backend before any signed-driver runtime test is allowed.
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-kernel-pcm-client
        if ($LASTEXITCODE -ne 0) { throw 'User-mode kernel PCM protocol test failed' }
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-kernel-pcm-worker
        if ($LASTEXITCODE -ne 0) { throw 'Virtual PCM worker/coordinator lifecycle test failed' }
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-virtual-bus-recovery
        if ($LASTEXITCODE -ne 0) { throw 'Multi-process PCM crash recovery test failed' }
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-local-broker
        if ($LASTEXITCODE -ne 0) { throw 'Same-user Named Pipe mock broker test failed' }
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-local-broker-restart
        if ($LASTEXITCODE -ne 0) { throw 'Mock broker crash/restart recovery test failed' }
        # The installer helper is checked in READ-ONLY mode. Never stage the
        # unsigned WDK SYS or developer helper into the Portable product.
        & (Join-Path $root 'scripts\build-virtual-driver-setup.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Read-only Driver Setup preflight tests failed' }
        & (Join-Path $root 'scripts\test-multiple-portable-instances.ps1') -SkipBuild
        if ($LASTEXITCODE -ne 0) { throw 'Cross-folder Virtual Driver audio-sharing test failed' }
        & dotnet run --no-build -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj' -- --test-virtual-driver-discovery
        if ($LASTEXITCODE -ne 0) { throw 'Virtual Driver installation-readiness and PnP inventory tests failed' }
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
        Copy-Item -LiteralPath (Join-Path $root "docs\releases\v$version.md") -Destination (Join-Path $target 'RELEASE_NOTES.md') -Force
        $licenseFolder = Join-Path $target 'licenses'
        New-Item -ItemType Directory -Force -Path $licenseFolder | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'third_party\rnnoise\COPYING') -Destination (Join-Path $licenseFolder 'RNNoise-v0.1.1-BSD-3-Clause.txt') -Force

        $archive = Join-Path $root ("dist\AuralWeave-v$version-Windows-x64.zip")
        Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive -CompressionLevel Optimal -Force
        if (!(Test-Path -LiteralPath $archive)) { throw 'Portable ZIP was not created' }
        Write-Output "Native-only portable ZIP: $archive"
        Write-Output "Native-only portable output: $target"
    }
}
finally { Pop-Location }
