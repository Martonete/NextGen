$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

docker compose up -d
docker compose ps

Write-Host ""
Write-Host "Servidor local listo en 127.0.0.1:5028"
Write-Host "pgAdmin: http://localhost:5050"
