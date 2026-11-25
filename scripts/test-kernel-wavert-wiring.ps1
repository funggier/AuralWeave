# Structural regression for the UNSIGNED WaveRT kernel prototype.
# This does not start, install, or open a Windows kernel audio device.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Require([string]$relative, [string]$pattern, [string]$message) {
    $file = Join-Path $root $relative
    $content = Get-Content -LiteralPath $file -Raw
    if ($content -notmatch $pattern) { throw "Kernel PCM regression: $message ($relative)" }
}
function Reject([string]$relative, [string]$pattern, [string]$message) {
    $file = Join-Path $root $relative
    $content = Get-Content -LiteralPath $file -Raw
    if ($content -match $pattern) { throw "Kernel PCM unsafe legacy code: $message ($relative)" }
}

$adapter = 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\adapter.cpp'
$common = 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\common.cpp'
$stream = 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\minwavertstream.cpp'
$project = 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\Main.vcxproj'
$util = 'src\AuralWeave.VirtualDriver\Kernel\Source\Utilities\Utilities.vcxproj'
$queue = 'src\AuralWeave.VirtualDriver\Kernel\Source\Main\aw_kernel_pcm_queue.cpp'
$installer = 'src\AuralWeave.Audio.Windows\VirtualDriverInstallation.cs'

Require $adapter 'AwKernelPcmInitialize\s*\(' 'ring not initialized before PortCls'
Require $adapter 'AwRoleOutput[\s\S]*InstallAllRenderFilters' 'Virtual Output root role not routed to render filters'
Require $adapter 'AwRoleInput[\s\S]*InstallAllCaptureFilters' 'Virtual Input root role not routed to capture filters'
Require $common 'InterlockedIncrement\s*\([\s\S]*?m_AdapterInstances' 'second root adapter is still blocked'
Require $common 'instanceCount\s*>\s*2' 'adapter count no longer limited to two'
Reject $common 'CSaveData|InitializeWorkItems|DestroyWorkItems' 'obsolete file recording workers'
Require $stream 'AwKernelPcmRead\s*\(\s*AW_PCM_WIRE_INPUT' 'capture DMA not consuming virtual microphone PCM'
Require $stream 'AwKernelPcmWrite\s*\(\s*AW_PCM_WIRE_OUTPUT' 'render DMA not publishing virtual speaker PCM'
Require $stream 'RtlZeroMemory\s*\(\s*destination,\s*runBytes\s*\)' 'capture underrun silence missing'
Require $stream 'm_AwRenderCopyProtected' 'DRM protected content guard missing'
Require $stream 'AwKernelPcmDisableOutput\s*\(' 'DRM right change does not disable output ring'
Require $queue 'endpoint->blocked = true' 'DRM output queue does not latch write rejection'
Require $queue '!endpoint->blocked' 'DRM output queue write race remains'
Reject $stream 'GenerateSine\s*\(|m_SaveData\.|ToneGenerator\.Init' 'legacy tone or file capture reachable'
Require $queue 'KeAcquireSpinLock' 'kernel PCM buffer lock missing'
Require $queue 'aw_pcm_ring_write' 'bounded output ring missing'
Require $queue 'aw_pcm_ring_read' 'bounded input ring missing'
Require $project 'aw_kernel_pcm_queue\.cpp' 'kernel PCM implementation not built in WDK'
Reject $util 'ClCompile Include="savedata.cpp"|ClCompile Include="tonegenerator.cpp"' 'legacy recording/tone sources still linked'
Require $installer 'ApprovedInstallerCertificateSha256\s*=\s*""' 'missing fail-closed production publisher pin'
$sys = @(git -C $root ls-files '*.sys')
if ($sys.Count -ne 0) { throw 'Unsigned SYS must not be tracked in Git.' }
Write-Output 'WAVERT_INPUT_OUTPUT_PCM_ROUTING=SOURCE_VERIFIED'
Write-Output 'TWO_ROOT_ADAPTERS_AND_NO_LEGACY_RECORDING=SOURCE_VERIFIED'
Write-Output 'DRM_RING_FLUSH_AND_SAFE_INSTALL_GATING=SOURCE_VERIFIED'
Write-Warning 'Structural checks are not a kernel runtime or driver signing test.'
