param(
    [string]$Source = "",
    [string]$Destination = ""
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$Source) { $Source = Join-Path $root 'src\AuralWeave.Desktop\Assets\AuralWeave-Monochrome.png' }
if (!$Destination) { $Destination = Join-Path $root 'src\AuralWeave.Desktop\Assets\AuralWeave.ico' }
Add-Type -AssemblyName System.Drawing
$sizes = @(16,24,32,48,64,128,256)
$original = [System.Drawing.Image]::FromFile($Source)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($original,0,0,$size,$size)
            } finally { $graphics.Dispose() }
            $memory = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($memory.ToArray())
            } finally { $memory.Dispose() }
        } finally { $bitmap.Dispose() }
    }
} finally { $original.Dispose() }
$stream=[System.IO.File]::Create($Destination)
try {
    $writer=New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++){
            $size=$sizes[$i]
            $writer.Write([byte]($size % 256))
            $writer.Write([byte]($size % 256))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write([byte[]]$frame)}
    } finally {$writer.Dispose()}
} finally {$stream.Dispose()}
Write-Output "ICO=$Destination"
Write-Output "FRAMES=$($sizes -join ',')"
Write-Output "ICO_SHA256=$((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash)"
