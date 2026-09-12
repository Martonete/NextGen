# The 32px-stamp terrain was rejected. Keep the old command as an alias only.
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Import-ReimaginedTerrain.ps1')
