$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $repo 'resources/art-source/terrain-refresh'
$materials = @{}
try {
    foreach ($name in 'grass','earth') {
        $src = [Drawing.Bitmap]::new((Join-Path $archive "slate-village/$name.png"))
        $dst = [Drawing.Bitmap]::new(512,512)
        $g = [Drawing.Graphics]::FromImage($dst)
        $attr = [Drawing.Imaging.ImageAttributes]::new()
        try {
            $attr.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($src,[Drawing.Rectangle]::new(0,0,512,512),0,0,$src.Width,$src.Height,[Drawing.GraphicsUnit]::Pixel,$attr)
        } finally { $g.Dispose(); $attr.Dispose(); $src.Dispose() }
        $materials[$name] = $dst
    }
    foreach ($id in 6000..6009) {
        $original = [Drawing.Bitmap]::new((Join-Path $archive "original/$id.png"))
        $dst = [Drawing.Bitmap]::new($original.Width,$original.Height)
        try {
            for ($y=0; $y -lt $original.Height; $y++) {
                for ($x=0; $x -lt $original.Width; $x++) {
                    $p = $original.GetPixel($x,$y)
                    if ($p.R -le 3 -and $p.G -le 3 -and $p.B -le 3) {
                        $dst.SetPixel($x,$y,$p); continue
                    }
                    # Same native-coordinate coverage as the preceding terrain version.
                    # Generated artwork supplies materials ONLY, never road placement.
                    $coverage = if ($id -lt 6005) { 0.0 } else { [Math]::Min(1.0,[Math]::Max(0.0,($p.R-$p.G+6)/18.0)) }
                    $a = $materials['grass'].GetPixel($x,$y)
                    $b = $materials['earth'].GetPixel($x,$y)
                    $shade = 1.0
                    if ($id -gt 6000 -and $id -lt 6005) {
                        $shade = [Math]::Min(1.0,[Math]::Max(0.65,($p.R+$p.G+$p.B)/110.0))
                    }
                    $r = [int]($a.R*$shade*(1-$coverage)+$b.R*$coverage)
                    $green = [int]($a.G*$shade*(1-$coverage)+$b.G*$coverage)
                    $blue = [int]($a.B*$shade*(1-$coverage)+$b.B*$coverage)
                    $dst.SetPixel($x,$y,[Drawing.Color]::FromArgb($p.A,[Math]::Max(4,$r),[Math]::Max(4,$green),[Math]::Max(4,$blue)))
                }
            }
            $dst.Save((Join-Path $repo "resources/data/Graficos/$id.png"),[Drawing.Imaging.ImageFormat]::Png)
            Write-Output "${id}: shared generated materials, original coverage and dimensions."
        } finally { $original.Dispose(); $dst.Dispose() }
    }
} finally { foreach ($material in $materials.Values) { $material.Dispose() } }
