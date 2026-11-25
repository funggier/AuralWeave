# Prepare a Windows 10 x64 lab submission for Microsoft attestation.
# The CAB stays in the development build tree.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\x64\Release'
$base = 'AuralWeaveVirtualAudio'
$components = @("$base.inf", "$base.sys", "$base.pdb", "$base.cat")
$inf2cat = Join-Path $env:ProgramFiles 'Windows Kits\10\bin\10.0.28000.0\x86\Inf2Cat.exe'
if (!(Test-Path -LiteralPath $inf2cat -PathType Leaf)) {
    $inf2cat = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.28000.0\x86\Inf2Cat.exe'
}
if (!(Test-Path -LiteralPath $inf2cat -PathType Leaf)) { throw 'WDK Inf2Cat not found.' }

& (Join-Path $PSScriptRoot 'build-virtual-driver-prototype.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Kernel prototype build failed.' }

$id = [guid]::NewGuid().ToString('N')
$work = Join-Path $root "build\virtual-driver-attestation\$id"
$package = Join-Path $work $base
$verify = Join-Path $work 'extracted'
New-Item -ItemType Directory -Path $package, $verify -Force | Out-Null

foreach ($name in $components[0..2]) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $package
}
& $inf2cat "/driver:$package" '/os:10_VB_X64' '/uselocaltime'
if ($LASTEXITCODE -ne 0) { throw 'Windows 10 x64 catalog generation failed.' }

$archiveName = "$base-Win10-x64-Attestation-Unsigned.cab"
$archive = Join-Path $work $archiveName
$directives = @(
    '.OPTION EXPLICIT'
    ".Set CabinetNameTemplate=$archiveName"
    '.Set DiskDirectoryTemplate=.'
    '.Set CompressionType=MSZIP'
    '.Set Cabinet=ON'
    '.Set Compress=ON'
    ".Set DestinationDir=$base"
)
foreach ($name in $components) {
    $directives += '"' + $base + '\' + $name + '"'
}
$ddf = Join-Path $work 'submission.ddf'
Set-Content -LiteralPath $ddf -Value $directives -Encoding Ascii

Push-Location $work
try {
    & makecab.exe /F $ddf
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw 'MakeCab did not produce the attestation input.'
    }

    & expand.exe '-F:*' $archive $verify
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read back CAB contents.' }
    $files = @(Get-ChildItem -LiteralPath $verify -Recurse -File)
    if ($files.Count -ne $components.Count) { throw 'Unexpected file count in CAB.' }
    foreach ($name in $components) {
        $extracted = Join-Path (Join-Path $verify $base) $name
        $expected = (Get-FileHash -LiteralPath (Join-Path $package $name) -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $extracted -Algorithm SHA256).Hash -ne $expected) {
            throw "CAB verification failed for $name."
        }
    }

    Write-Output "ATTESTATION_INPUT_CAB=$archive"
    Write-Output "ATTESTATION_INPUT_SHA256=$((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)"
    Write-Output "UNSIGNED_DRIVER_PACKAGE=$package"
    Write-Warning 'Lab submission input. The signed file returned by Microsoft is required for Windows endpoint testing.'
}
finally {
    Pop-Location
}
