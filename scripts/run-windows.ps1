$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not $env:JOBRADAR_DB) { Write-Warning 'Set JOBRADAR_DB with PostgreSQL connection details first; do not commit secrets.' }
dotnet run --project src/JobRadar/JobRadar.csproj -- --once