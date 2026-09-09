# Compatibility entry point: independent sheet resizing caused terrain seams.
param([string]$GeneratedDirectory)
if ($GeneratedDirectory -and (Resolve-Path $GeneratedDirectory).Path -ne (Resolve-Path (Join-Path $PSScriptRoot '../resources/art-source/terrain-refresh/generated')).Path) {
    throw 'Use the archived shared materials in resources/art-source/terrain-refresh/generated.'
}
& (Join-Path $PSScriptRoot 'Compile-TerrainMaterials.ps1')
