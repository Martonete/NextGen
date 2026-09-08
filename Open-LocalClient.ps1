param([switch]$ForceBuild)
$ErrorActionPreference = "Stop"

$repo = $PSScriptRoot
$godot = Join-Path (Split-Path $repo -Parent) "Godot_4.4_mono\Godot_v4.4-stable_mono_win64\Godot_v4.4-stable_mono_win64.exe"

if (-not (Test-Path $godot)) {
    throw "No encontre Godot 4.4 .NET en: $godot"
}

$clientPath = Join-Path $repo "client"
$assemblyPath = Join-Path $clientPath ".godot\mono\temp\bin\Debug\ArgentumNextgen.dll"
$needsBuild = $ForceBuild -or -not (Test-Path -LiteralPath $assemblyPath)
if (-not $needsBuild) {
    $assemblyTime = (Get-Item -LiteralPath $assemblyPath).LastWriteTimeUtc
    $inputs = @(
        Get-ChildItem -LiteralPath (Join-Path $clientPath "Scripts") -Filter *.cs -Recurse -File
        Get-ChildItem -LiteralPath $clientPath -File | Where-Object { $_.Extension -in '.csproj', '.props', '.targets', '.cs' }
        Get-ChildItem -LiteralPath (Join-Path $repo "resources\compressor\lib") -Recurse -File |
            Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.targets' -and $_.FullName -notmatch '\\(bin|obj)\\' }
    )
    $needsBuild = @($inputs | Where-Object { $_.LastWriteTimeUtc -gt $assemblyTime }).Count -gt 0
}
if ($needsBuild) {
    Push-Location $clientPath
    try {
        dotnet build
        if ($LASTEXITCODE -ne 0) { throw "La compilacion fallo; no se abrira un cliente desactualizado." }
    } finally { Pop-Location }
}
# GUI executable, detached from PowerShell. Fullscreen is established before loading.
Start-Process -FilePath $godot -WorkingDirectory $clientPath -ArgumentList @('--path', ('"' + $clientPath + '"'), '--fullscreen', '--', '--start-fullscreen') -WindowStyle Normal
