# Compatibility entry point for the current full-sheet artwork.
param([string]$GeneratedDirectory)
if ($GeneratedDirectory -and (Resolve-Path $GeneratedDirectory).Path -ne (Resolve-Path (Join-Path $PSScriptRoot '../resources/art-source/terrain-refresh/generated')).Path) {
    throw 'The current import uses the archived originals and reimagined full sheets.'
}
& (Join-Path $PSScriptRoot 'Import-ReimaginedTerrain.ps1')
