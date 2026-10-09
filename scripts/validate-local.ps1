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
    else {
        # Fail fast if the configured TCP endpoint is unavailable. This is a
        # reachability check only; the tests still verify DB/auth/schema.
        # Never print the password or full connection string.
        $parts = [System.Data.Common.DbConnectionStringBuilder]::new()
        $parts.ConnectionString = $env:JOBRADAR_TEST_DB
        # DbConnectionStringBuilder key lookup may be case-sensitive. In particular,
        # the supported connection string often uses lowercase 'host' and 'port'.
        # Read keys case-insensitively and require both fields instead of silently
        # probing an unrelated default server on localhost:5432.
        $hostName = $null
        $portNumber = $null
        foreach ($key in $parts.Keys) {
            if ([string]$key -ieq 'host') { $hostName = [string]$parts[$key] }
            if ([string]$key -ieq 'port') { $portNumber = [int]$parts[$key] }
        }
        if ([string]::IsNullOrWhiteSpace($hostName) -or $null -eq $portNumber) {
            throw 'JOBRADAR_TEST_DB must specify explicit Host and Port for TCP preflight.'
        }
        if ($hostName.Contains(',') -or $hostName.Contains('/')) {
            throw 'Local test DB preflight requires a single TCP Host. Use an explicit host and port.'
        }
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $attempt = $client.ConnectAsync($hostName,$portNumber)
            if (-not $attempt.Wait(3000)) {
                throw 'TCP connection timed out'
            }
            Write-Host ('PostgreSQL TCP endpoint reachable: ' + $hostName + ':' + $portNumber)
        }
        catch {
            throw ('PostgreSQL TCP preflight failed at ' + $hostName + ':' + $portNumber +
              '. Check the PostgreSQL service, host/port in DBeaver, firewall, or Docker port binding. ' +
              'Database tests were not started. Cause: ' + $_.Exception.Message)
        }
        finally {
            $client.Dispose()
        }
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
