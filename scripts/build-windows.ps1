param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$RunDeviceDiagnostics,
    [switch]$PublishPortable
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    $projects = @(
        'src/AuralWeave.Core/AuralWeave.Core.csproj',
        'src/AuralWeave.Desktop/AuralWeave.Desktop.csproj',
        'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    )
    foreach ($p in $projects) {
        & dotnet build $p -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
    }
    & dotnet run --no-build -c $Configuration --project 'tests/AuralWeave.Core.Tests/AuralWeave.Core.Tests.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }

    if ($RunDeviceDiagnostics) {
        & dotnet run -c $Configuration --project 'src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj'
        if ($LASTEXITCODE -ne 0) { throw 'Device diagnostics failed' }
    }
    if ($PublishPortable) {
        $dest = Join-Path $root 'dist/AuralWeave-win-x64'
        & dotnet publish 'src/AuralWeave.Desktop/AuralWeave.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dest --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
        Write-Output "Portable output directory: $dest"
    }
}
finally { Pop-Location }
