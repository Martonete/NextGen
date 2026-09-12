$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=Split-Path $PSScriptRoot -Parent
foreach($id in 6000..6009){
    $a=[Drawing.Bitmap]::new((Join-Path $repo "resources/art-source/terrain-refresh/original/$id.png"))
    $b=[Drawing.Bitmap]::new((Join-Path $repo "resources/data/Graficos/$id.png"))
    try {
        if($a.Size -ne $b.Size){throw "Dimensions differ: $id"}
        for($y=0;$y -lt $a.Height;$y++){for($x=0;$x -lt $a.Width;$x++){
            $p=$a.GetPixel($x,$y);$q=$b.GetPixel($x,$y)
            $oldKey=$p.R -le 3 -and $p.G -le 3 -and $p.B -le 3
            $newKey=$q.R -le 3 -and $q.G -le 3 -and $q.B -le 3
            if($oldKey -ne $newKey -or $p.A -ne $q.A){throw "Transparency differs: $id at $x,$y"}
        }}
        Write-Output "${id}: dimensions and every transparency pixel PASS"
    } finally {$a.Dispose();$b.Dispose()}
}
