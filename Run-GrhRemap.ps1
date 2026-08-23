# Compacta Graficos.ind: descarta el arte viejo y renumera el nuevo desde 1.
#
# Este es el paso irreversible del reinicio del catalogo. Antes de escribir
# muestra que va a hacer y pide confirmacion. Deja respaldo en Graficos.ind.bak
# y emite docs/catalog/remap.json con el mapa viejo->nuevo, que es lo que
# permite rehacer las tablas de entidades sin volver a indexar.
#
# Uso:   powershell -ExecutionPolicy Bypass -File .\Run-GrhRemap.ps1

$ErrorActionPreference = 'Stop'

$repo    = $PSScriptRoot
$exe     = Join-Path $repo 'tools\grhtool\bin\Debug\net8.0-windows\grhtool.exe'
$ind     = Join-Path $repo 'resources\data\INIT\Graficos.ind'
$remap   = Join-Path $repo 'docs\catalog\remap.json'
$graficos= Join-Path $repo 'resources\data\Graficos'

if (-not (Test-Path $exe)) {
    Write-Host "No existe $exe" -ForegroundColor Red
    Write-Host "Compilalo con:  dotnet build `"$repo\tools\grhtool`"" -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path $ind)) { Write-Host "No existe $ind" -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host '=== Previsualizacion (no escribe nada) ===' -ForegroundColor Cyan
& $exe remap $ind $remap
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host "grhtool aborto (codigo $LASTEXITCODE). No se modifico nada." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ''
Write-Host 'Esto reescribe Graficos.ind y descarta el arte viejo.' -ForegroundColor Yellow
Write-Host "Respaldo automatico en: $ind.bak" -ForegroundColor Yellow
$resp = Read-Host 'Escribi SI para aplicar (cualquier otra cosa cancela)'
if ($resp -ne 'SI') { Write-Host 'Cancelado, no se modifico nada.'; exit 0 }

Write-Host ''
Write-Host '=== Aplicando ===' -ForegroundColor Cyan
& $exe remap $ind $remap --apply
if ($LASTEXITCODE -ne 0) { Write-Host "Fallo al aplicar (codigo $LASTEXITCODE)." -ForegroundColor Red; exit $LASTEXITCODE }

Write-Host ''
Write-Host '=== Verificacion posterior ===' -ForegroundColor Cyan
& $exe verify $ind --graficos $graficos

Write-Host ''
Write-Host 'Listo. Para volver atras:' -ForegroundColor Green
Write-Host "  Copy-Item `"$ind.bak`" `"$ind`" -Force"
