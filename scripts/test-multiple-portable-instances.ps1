# Validates one Virtual Input and one Virtual Output controller across
# distinct Portable app folders using Windows Global named mutex leases.
# Runs managed Diagnostics only; NEVER installs/removes a Windows driver.
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    if (!$SkipBuild) {
        & dotnet build 'src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj' -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed Diagnostics build failed.' }
    }
    $source = Join-Path $root 'src\AuralWeave.Diagnostics\bin\Release\net8.0-windows'
    $origin = Join-Path $source 'AuralWeave.Diagnostics.dll'
    if (!(Test-Path -LiteralPath $origin)) { throw 'Diagnostics DLL missing.' }

    # The second copy is safely isolated in the project's ignored build\
    # directory, never in the user's separately maintained Portable Products.
    $alternative = Join-Path $root 'build\multiple-portable-folders\Portable-B'
    New-Item -ItemType Directory -Force -Path $alternative | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'AuralWeave.Diagnostics.dll') -Destination $alternative -Force
    Copy-Item -LiteralPath (Join-Path $source 'AuralWeave.Diagnostics.runtimeconfig.json') -Destination $alternative -Force
    Copy-Item -LiteralPath (Join-Path $source 'AuralWeave.Diagnostics.deps.json') -Destination $alternative -Force
    foreach ($name in @('AuralWeave.Core.dll', 'AuralWeave.Audio.Windows.dll')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $alternative -Force
    }
    $alternateDll = Join-Path $alternative 'AuralWeave.Diagnostics.dll'
    Write-Output "PARENT_FOLDER=$source"
    Write-Output "SECOND_FOLDER=$alternative"
    if ($source -eq $alternative) { throw 'Portable copies must be in different folders.' }
    $old = [Environment]::GetEnvironmentVariable('AW_LEASE_PROBE_DLL','Process')
    try {
        [Environment]::SetEnvironmentVariable('AW_LEASE_PROBE_DLL', $alternateDll, 'Process')
        & dotnet $origin --test-virtual-device-ownership
        if ($LASTEXITCODE -ne 0) {
            throw "Cross-folder driver lease coordination failed: $LASTEXITCODE"
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('AW_LEASE_PROBE_DLL', $old, 'Process')
    }
    Write-Output 'TWO_DIFFERENT_PORTABLE_FOLDERS_INPUT_OUTPUT_OWNERSHIP=PASS'
}
finally { Pop-Location }
