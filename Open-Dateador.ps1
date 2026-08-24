# Abre el AO Dateador: editor de objetos, NPCs, hechizos, experiencia y recetas.
#
# El tab de Objetos muestra el grafico de cada item y permite elegirlo de una
# galeria filtrada por clase. Guarda editando obj.dat en el lugar, sin tocar
# comentarios ni los campos que la herramienta no modela, y sincroniza las
# copias de resources/ y client/.

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

$dateadorDir = Join-Path $repo "tools\dateador"

Set-Location $dateadorDir
dotnet build .\Dateador.csproj
& $godot --path .
