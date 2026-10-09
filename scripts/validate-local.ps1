param(
    [switch]$RequirePostgres
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install .NET 10 SDK locally. No GitHub Actions workflow will run.'
    }
    Write-Host 'JobRadar: local-only validation; GitHub Actions disabled.'
    & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet SDK check failed.' }

    $dbConfigured = -not [string]::IsNullOrWhiteSpace($env:JOBRADAR_TEST_DB)
    if ($RequirePostgres -and -not $dbConfigured) {
        throw 'JOBRADAR_TEST_DB must be set when -RequirePostgres is specified.'
    }
    if (-not $dbConfigured) {
        Write-Warning 'PostgreSQL integration tests will skip their DB work; no full integration pass is claimed.'
    }

    $project = 'tests/JobRadar.Tests/JobRadar.Tests.csproj'
    & dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet test $project -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    Write-Host ('Validation complete. PostgreSQL tests enabled: ' + $dbConfigured)
}
finally {
    Pop-Location
}
