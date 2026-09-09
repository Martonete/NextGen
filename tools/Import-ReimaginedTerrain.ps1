$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=Split-Path $PSScriptRoot -Parent
$archive=Join-Path $repo 'resources/art-source/terrain-refresh'
$sources=Import-Csv (Join-Path $archive 'reimagined/sources.csv')
$images=@{}
try {
    foreach($entry in $sources) {
        $id=[int]$entry.Id
        $src=[Drawing.Bitmap]::new((Join-Path $archive "reimagined/$($entry.File)"))
        $size=if($id -eq 6009){128}else{512}
        $dst=[Drawing.Bitmap]::new($size,$size)
        $g=[Drawing.Graphics]::FromImage($dst)
        $attr=[Drawing.Imaging.ImageAttributes]::new()
        try {
            $attr.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($src,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$src.Width,$src.Height,[Drawing.GraphicsUnit]::Pixel,$attr)
        } finally {$g.Dispose();$attr.Dispose();$src.Dispose()}
        $images[$id]=$dst
    }
    foreach($id in 6000..6009) {
        $original=[Drawing.Bitmap]::new((Join-Path $archive "original/$id.png"))
        $dst=[Drawing.Bitmap]::new($original.Width,$original.Height)
        try {
            for($y=0;$y -lt $original.Height;$y++) { for($x=0;$x -lt $original.Width;$x++) {
                $p=$original.GetPixel($x,$y)
                if($p.R -le 3 -and $p.G -le 3 -and $p.B -le 3){$dst.SetPixel($x,$y,$p);continue}
                # Keep coverage in the ORIGINAL native coordinate system. Generated
                # contours are never used to place the grass/earth transition.
                $coverage=if($id -lt 6005){0.0}else{[Math]::Clamp(($p.R-$p.G+6)/18.0,0.0,1.0)}
                # Full 512x128 artwork retains natural variation, not a 32px stamp.
                $a=$images[6000].GetPixel($x%512,[Math]::Clamp($y,1,126))
                $b=$images[$id].GetPixel($x,[Math]::Clamp($y,1,126))
                if($b.R -lt $b.G -or $id -lt 6005){$b=$images[6009].GetPixel(32+($x%64),32+($y%64))}
                # Preserve original depressions without interpreting them as dirt.
                $shade=1.0
                if($id -gt 6000 -and $id -lt 6005){$shade=[Math]::Clamp(($p.R+$p.G+$p.B)/110.0,0.65,1.0)}
                $r=[int]($a.R*$shade*(1-$coverage)+$b.R*$coverage)
                $green=[int]($a.G*$shade*(1-$coverage)+$b.G*$coverage)
                $blue=[int]($a.B*$shade*(1-$coverage)+$b.B*$coverage)
                $dst.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,[Math]::Max(4,$r),[Math]::Max(4,$green),[Math]::Max(4,$blue)))
            } }
            $dst.Save((Join-Path $repo "resources/data/Graficos/$id.png"),[Drawing.Imaging.ImageFormat]::Png)
            Write-Output "${id}: native geometry and dimensions, full-sheet artwork."
        } finally {$original.Dispose();$dst.Dispose()}
    }
} finally {foreach($im in $images.Values){$im.Dispose()}}
