# Erzeugt das App-Symbol (Assets\fledgr.ico): transparentes, flaches Wave-Play Icon in leuchtendem Gelb/Gold.
# Aufruf: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\YTSPlayer.Desktop\Assets\fledgr.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 6 vertikale Equalizer-Balken, die eine dynamische Play-Pfeilform bilden
    $bars = @(
        @{ xRel = 0.20; hRel = 0.62 },
        @{ xRel = 0.32; hRel = 0.76 },
        @{ xRel = 0.44; hRel = 0.68 },
        @{ xRel = 0.56; hRel = 0.52 },
        @{ xRel = 0.68; hRel = 0.36 },
        @{ xRel = 0.80; hRel = 0.20 }
    )

    $barW = [Math]::Max(1.5, $s * 0.082)

    for ($i = 0; $i -lt $bars.Count; $i++) {
        $b = $bars[$i]
        $x = $s * $b.xRel - ($barW / 2)
        $h = [Math]::Max($barW, $s * $b.hRel)
        $y = ($s - $h) / 2
        $t = $i / ($bars.Count - 1)

        # Farbverlauf von strahlendem Sonnengelb (255, 225, 20) zu warmem Bernsteingold (255, 150, 0)
        $r = 255
        $gCol = [int](225 - 75 * $t)
        $bVal = [int](20 - 20 * $t)
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $r, $gCol, $bVal))

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, $barW, $barW, 180, 180)
        $path.AddArc($x, $y + $h - $barW, $barW, $barW, 0, 180)
        $path.CloseFigure()

        $g.FillPath($brush, $path)
        $brush.Dispose()
        $path.Dispose()
    }

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$frames = foreach ($s in $sizes) { , (New-Frame $s) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bytes = $frames[$i]
    $w.Write([byte]($s -band 255)); $w.Write([byte]($s -band 255)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($b in $frames) { $w.Write($b) }
$w.Dispose(); $fs.Dispose()
Write-Output "Geschrieben: $out"
