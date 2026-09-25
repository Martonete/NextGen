# Importa el terreno de BlisseAO (12052-12061.BMP) sobre nuestras laminas 6000-6009.
#
# Los GRH 6000+ de Blisse (grh.ini) apuntan a sus archivos 12052..12061 con los mismos
# rectangulos que nuestros GRH 6000+ (Graficos.ini) sobre 6000..6009. Por eso el reemplazo
# es 1:1 por posicion: 12052 -> 6000, 12053 -> 6001, ..., 12061 -> 6009.
# NO se tocan nuestros 12052-12061.png (son graficos distintos).
#
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools/Import-BlisseTerrain.ps1 [-Source <dir>]
param(
    [string]$Source = "C:\Users\marti\Desktop\Nueva carpeta\BlisseAO-master\Cliente\Resources\Graficos"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'resources\data\Graficos'
$archive = Join-Path $root 'resources\art-source\terrain-refresh\blisse'
$before = Join-Path $archive 'before'
$src = Join-Path $archive 'source'
New-Item -ItemType Directory -Force $before, $src | Out-Null

for ($i = 0; $i -lt 10; $i++) {
    $blisse = 12052 + $i
    $ours = 6000 + $i
    $bmpPath = Join-Path $Source "$blisse.BMP"
    if (-not (Test-Path $bmpPath)) { throw "Falta $bmpPath" }
    $pngPath = Join-Path $dest "$ours.png"

    # Archivar el original de Blisse y nuestra version anterior (una sola vez).
    Copy-Item $bmpPath (Join-Path $src "$blisse.BMP") -Force
    $bak = Join-Path $before "$ours.png"
    if ((Test-Path $pngPath) -and -not (Test-Path $bak)) { Copy-Item $pngPath $bak }

    # BMP 24bpp -> PNG 32bpp. El negro puro queda como esta: el cliente lo usa como color key.
    $bmp = [System.Drawing.Bitmap]::FromFile($bmpPath)
    try {
        $out = New-Object System.Drawing.Bitmap $bmp.Width, $bmp.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($out)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.DrawImage($bmp, 0, 0, $bmp.Width, $bmp.Height)
        $g.Dispose()
        $out.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $out.Dispose()
        Write-Host ("{0}.BMP ({1}x{2}) -> {3}" -f $blisse, $bmp.Width, $bmp.Height, $pngPath)
    } finally { $bmp.Dispose() }
}
Write-Host "Listo. Regenerar client/Data/graficos.aopak con aopak-cli pack."
