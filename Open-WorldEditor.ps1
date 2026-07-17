$ErrorActionPreference = "Stop"

$repo = $PSScriptRoot
$godot43 = Join-Path $env:USERPROFILE "Desktop\Godot_4.3_mono\Godot_v4.3-stable_mono_win64\Godot_v4.3-stable_mono_win64.exe"
$godot44 = Join-Path $repo "Godot_4.4_mono\Godot_v4.4-stable_mono_win64\Godot_v4.4-stable_mono_win64.exe"

if (Test-Path $godot43) {
    $godot = $godot43
} elseif (Test-Path $godot44) {
    $godot = $godot44
} else {
    throw "No encontre Godot .NET. Esperaba: $godot43 o $godot44"
}

$editorDir = Join-Path $repo "tools\world-editor"

Set-Location $editorDir
dotnet build .\WorldEditor.csproj
& $godot --path .
