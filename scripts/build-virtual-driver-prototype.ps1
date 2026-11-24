# Build and validate an UNSIGNED AuralWeave kernel audio prototype.
# No certificates, no catalog creation, no deployment, no test-signing.
# Windows 10 22H2 and Windows 11 x64 are TARGETS; runtime compatibility
# must still be verified using HLK and clean VMs before any distribution.
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'check-driver-toolchain.ps1') -Strict
    if ($LASTEXITCODE -ne 0) { throw 'WDK environment unavailable.' }

    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = (& $vswhere -all -products '*' -requires 'Component.Microsoft.Windows.DriverKit' -property installationPath |
        Select-Object -First 1)
    if (!$vs) { throw 'WDK integration is not installed in Visual Studio.' }
    $msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
    $project = Join-Path $root 'src\AuralWeave.VirtualDriver\Kernel\AuralWeaveVirtualAudio.sln'
    # Verify that the kernel INF and the UI's exact 16-ID PnP bank
    # cannot silently diverge. IDs are stable across portable moves.
    $inx = Join-Path $root 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\AuralWeaveVirtualAudio.inx'
    $lines = @(Get-Content -LiteralPath $inx)
    $inModels = @($lines | Where-Object { $_ -match '^%AW\.Input\.[0-9]{4}%' })
    $outModels = @($lines | Where-Object { $_ -match '^%AW\.Output\.[0-9]{4}%' })
    if ($inModels.Count -ne 16 -or $outModels.Count -ne 16) {
        throw "Unexpected INF device count: Input=$($inModels.Count), Output=$($outModels.Count)."
    }
    for ($index = 1; $index -le 16; $index++) {
        $number = $index.ToString('0000')
        if (-not ($inModels | Where-Object { $_.EndsWith("ROOT\AURALWEAVE_VINPUT_$number") }) -or
            -not ($outModels | Where-Object { $_.EndsWith("ROOT\AURALWEAVE_VOUTPUT_$number") })) {
            throw "Missing unique AuralWeave virtual endpoint IDs for device $number"
        }
    }
    Write-Output 'INPUT_OUTPUT_HARDWARE_ID_BANK_16_EACH=PASS'
    $args = @(
        $project,
        "/p:Configuration=$Configuration",
        '/p:Platform=x64',
        '/p:WindowsTargetPlatformVersion=10.0.28000.0',
        '/p:SkipPackageVerification=true',
        '/p:ApiValidator_Enable=false',
        '/p:EnableInf2Cat=false',
        '/p:SignMode=Off',
        '/m:2',
        '/nologo',
        '/v:minimal'
    )
    & $msbuild @args
    if ($LASTEXITCODE -ne 0) { throw "Kernel compile failed ($LASTEXITCODE)." }

    $inputDir = Join-Path $root "src\AuralWeave.VirtualDriver\Kernel\Source\Main\x64\$Configuration"
    $stage = Join-Path $root "build\virtual-driver-prototype-package\$Configuration"
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($name in @('AuralWeaveVirtualAudio.inf', 'AuralWeaveVirtualAudio.sys')) {
        $sourceFile = Join-Path $inputDir $name
        if (!(Test-Path $sourceFile)) { throw "Missing kernel artifact: $sourceFile" }
        Copy-Item -LiteralPath $sourceFile -Destination $stage -Force
    }

    $kit = 'C:\Program Files (x86)\Windows Kits\10'
    $infverif = Join-Path $kit 'Tools\10.0.28000.0\x64\infverif.exe'
    $inf = Join-Path $stage 'AuralWeaveVirtualAudio.inf'
    foreach ($mode in @('/w','/u')) {
        & $infverif $mode $inf
        if ($LASTEXITCODE -ne 0) { throw "INF validation $mode failed ($LASTEXITCODE)." }
    }

    $inf2cat = Join-Path $kit 'bin\10.0.28000.0\x86\Inf2Cat.exe'
    # /nocat checks the package without generating a distributable CAT.
    # /uselocaltime avoids a temporary DriverVer "future date" mismatch
    # when WDK stamping is done shortly after Bangkok local midnight.
    & $inf2cat "/driver:$stage" '/os:10_VB_X64,10_CO_X64,10_NI_X64,10_GE_X64' '/nocat' '/uselocaltime'
    if ($LASTEXITCODE -ne 0) { throw "INF2CAT signability test failed ($LASTEXITCODE)." }

    $sys = Join-Path $stage 'AuralWeaveVirtualAudio.sys'
    $bytes = (Get-Item $sys).Length
    $sha = (Get-FileHash -LiteralPath $sys -Algorithm SHA256).Hash
    Write-Output "PROTOTYPE_SYS=$sys"
    Write-Output "SYS_BYTES=$bytes"
    Write-Output "SYS_SHA256=$sha"
    Write-Output 'WIN10_WIN11_X64_TARGET_METADATA_AND_SIGNABILITY=PASS'
    Write-Warning 'NOT INSTALLED, NOT MICROSOFT SIGNED, NOT RUNTIME TESTED. Kernel audio transport is not implemented. Do not copy to dist or install.'
}
finally {
    Pop-Location
}
