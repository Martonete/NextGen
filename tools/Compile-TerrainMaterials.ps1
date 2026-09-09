$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=Split-Path $PSScriptRoot -Parent
$archive=Join-Path $repo 'resources/art-source/terrain-refresh'
function Material($file,$rect) {
    $src=[Drawing.Bitmap]::new((Join-Path $archive "generated/$file"))
    $dst=[Drawing.Bitmap]::new(32,32); $g=[Drawing.Graphics]::FromImage($dst)
    try { $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $g.DrawImage($src,[Drawing.Rectangle]::new(0,0,32,32),$rect,[Drawing.GraphicsUnit]::Pixel) }
    finally { $g.Dispose();$src.Dispose() }
    return ,$dst
}
# Two shared materials; all geometry comes from native original masks.
$grass=Material 'exec-3f4edc4a-2722-459d-aa78-5f72ca33c7c8.png' ([Drawing.Rectangle]::new(100,80,80,80))
$soil=Material 'exec-5cb8f60e-8861-435c-8731-89a9a47d6f8f.png' ([Drawing.Rectangle]::new(460,460,320,320))
try {
    # Make both material swatches periodic at the GRH size, not atlas size.
    foreach($m in @($grass,$soil)) {
        foreach($axis in 0..1) { for($i=0;$i -lt 32;$i++) { for($d=0;$d -lt 4;$d++) {
            $w=(4-$d)/8.0
            $ax=if($axis -eq 0){$d}else{$i};$ay=if($axis -eq 0){$i}else{$d}
            $bx=if($axis -eq 0){31-$d}else{$i};$by=if($axis -eq 0){$i}else{31-$d}
            $a=$m.GetPixel($ax,$ay);$b=$m.GetPixel($bx,$by)
            $m.SetPixel($ax,$ay,[Drawing.Color]::FromArgb(255,[int]($a.R*(1-$w)+$b.R*$w),[int]($a.G*(1-$w)+$b.G*$w),[int]($a.B*(1-$w)+$b.B*$w)))
            $m.SetPixel($bx,$by,[Drawing.Color]::FromArgb(255,[int]($b.R*(1-$w)+$a.R*$w),[int]($b.G*(1-$w)+$a.G*$w),[int]($b.B*(1-$w)+$a.B*$w)))
        } } }
    }
    foreach($id in 6000..6009) {
        $old=[Drawing.Bitmap]::new((Join-Path $archive "original/$id.png"))
        $dst=[Drawing.Bitmap]::new($old.Width,$old.Height)
        try {
            for($y=0;$y -lt $old.Height;$y++) { for($x=0;$x -lt $old.Width;$x++) {
                $p=$old.GetPixel($x,$y)
                if($p.R -le 3 -and $p.G -le 3 -and $p.B -le 3){$dst.SetPixel($x,$y,$p);continue}
                $t=if($id -eq 6000){0.0}else{[Math]::Clamp(($p.R-$p.G+6)/18.0,0.0,1.0)}
                $a=$grass.GetPixel($x%32,$y%32);$b=$soil.GetPixel($x%32,$y%32)
                $dst.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,[int]($a.R*0.88*(1-$t)+$b.R*$t),[int]($a.G*0.88*(1-$t)+$b.G*$t),[int]($a.B*0.88*(1-$t)+$b.B*$t)))
            } }
            $dst.Save((Join-Path $repo "resources/data/Graficos/$id.png"),[Drawing.Imaging.ImageFormat]::Png)
            Write-Output "$id compiled from shared materials; grass -12%."
        } finally {$old.Dispose();$dst.Dispose()}
    }
} finally {$grass.Dispose();$soil.Dispose()}
