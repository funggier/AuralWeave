# Read-only verification of the driver package returned by Microsoft.
param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path -LiteralPath $PackageDirectory).Path
$base = 'AuralWeaveVirtualAudio'
$catalog = Join-Path $dir "$base.cat"
$signtool = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.28000.0\x64\signtool.exe'
foreach ($path in @($signtool, $catalog, (Join-Path $dir "$base.inf"), (Join-Path $dir "$base.sys"))) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}

foreach ($name in @("$base.inf", "$base.sys")) {
    $path = Join-Path $dir $name
    & $signtool verify /kp /v /c $catalog $path
    if ($LASTEXITCODE -ne 0) { throw "Windows kernel policy or catalog membership verification failed for $name." }
}
Write-Output 'KERNEL_POLICY_AND_CATALOG_MEMBERSHIP=PASS'
