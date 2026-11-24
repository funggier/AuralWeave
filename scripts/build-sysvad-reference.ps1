# Compiles Microsoft's pinned SysVAD reference ONLY, never installs a driver.
# Package validation & signing are deliberately disabled for the toolchain
# smoke test. A successful build here is NOT a production-ready driver.
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

& (Join-Path $PSScriptRoot 'check-driver-toolchain.ps1') -Strict
if ($LASTEXITCODE -ne 0) { throw 'Windows driver tools are not ready.' }

$reference = Join-Path $root 'build\sysvad-reference'
$solution = Join-Path $reference 'audio\sysvad\sysvad.sln'
if (!(Test-Path -LiteralPath $solution)) {
    throw "Missing Microsoft SysVAD source; see docs/DRIVER_DEVELOPMENT_SETUP.md."
}
$actualRevision = (& git -C $reference rev-parse HEAD).Trim()
$expectedRevision = 'd49bb7d72f3651952b2b6e4ad28d14fe2247eb69'
if ($actualRevision -ne $expectedRevision) {
    throw "SysVAD reference revision differs from the pinned revision: $actualRevision"
}
$wil = Join-Path $reference 'wil'
if (!(Test-Path -LiteralPath (Join-Path $wil 'include\wil\com.h'))) {
    throw 'Missing pinned microsoft/wil Git submodule.'
}
$actualWilRevision = (& git -C $wil rev-parse HEAD).Trim()
if ($actualWilRevision -ne '3c00e7f1d8cf9930bbb8e5be3ef0df65c84e8928') {
    throw "Unexpected WIL revision: $actualWilRevision"
}
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = (& $vswhere -all -products '*' -requires 'Component.Microsoft.Windows.DriverKit' -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($vs)) { throw 'Visual Studio WDK integration is missing.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
if (!(Test-Path -LiteralPath $msbuild)) { throw "MSBuild unavailable: $msbuild" }
$args = @(
    $solution,
    "/p:Configuration=$Configuration",
    '/p:Platform=x64',
    '/p:WindowsTargetPlatformVersion=10.0.28000.0',
    '/p:SkipPackageVerification=true',
    '/p:ApiValidator_Enable=false',
    '/p:EnableInf2Cat=false',
    '/p:SignMode=Off',
    '/m:2', '/nologo', '/v:minimal'
)
& $msbuild @args
if ($LASTEXITCODE -ne 0) { throw "Microsoft SysVAD compile-only build failed: $LASTEXITCODE" }
$sys = Join-Path $reference "audio\sysvad\TabletAudioSample\x64\$Configuration\TabletAudioSample.sys"
if (!(Test-Path -LiteralPath $sys)) { throw 'Expected sample SYS was not produced.' }
Write-Output "REFERENCE_KERNEL_SYS=$sys"
Write-Output "SIZE_BYTES=$((Get-Item -LiteralPath $sys).Length)"
Write-Output "SYS_SHA256=$((Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash)"
Write-Warning 'COMPILE-ONLY PASS: no INF validation, signing or installation. Do not distribute or install this reference SYS.'
