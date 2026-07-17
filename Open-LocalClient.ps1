$ErrorActionPreference = "Stop"

$repo = $PSScriptRoot
$godot = Join-Path (Split-Path $repo -Parent) "Godot_4.4_mono\Godot_v4.4-stable_mono_win64\Godot_v4.4-stable_mono_win64.exe"

if (-not (Test-Path $godot)) {
    throw "No encontre Godot 4.4 .NET en: $godot"
}

Set-Location (Join-Path $repo "client")
dotnet build
& $godot --path .
