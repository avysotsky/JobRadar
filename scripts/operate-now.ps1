# JobRadar initial operations: one controlled run of already supported sources.
# No GitHub Actions, Upwork scraping, proposals, Docker startup or unattended schedule.
param(
 [switch]$RunCrawler,
 [string]$UpworkFile,
 [switch]$SkipTriage,
 [switch]$SkipPending
)
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..'))
$project = '.\src\JobRadar\JobRadar.csproj'

if (-not (Test-Path $project)) {throw 'JobRadar project missing'}
if ([string]::IsNullOrWhiteSpace($env:JOBRADAR_DB)) {
 throw 'Set JOBRADAR_DB privately to the intended operational PostgreSQL database; never use jobradar_test.'
}
if ($env:JOBRADAR_DB -match '(?i)Database\s*=\s*jobradar_test(?:;|$)' -or
    $env:JOBRADAR_DB -match '(?i)CHANGE_ME') {
 throw 'Refusing test database or placeholder password for operations'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
 throw 'dotnet SDK not available in PATH'
}
if ($env:JOBRADAR_DB -notmatch '(?i)(^|;)\s*Database\s*=') {
 throw 'Explicit Database=... is required in JOBRADAR_DB for operational safety'
}

# Blocks another copy of THIS operator script, but does NOT block a separate
# Docker/container or direct --once process. Never launch those concurrently.
$mutex = [System.Threading.Mutex]::new($false,'Local\JobRadar-OperatorOnce')
$acquired = $false
try {
 $acquired = $mutex.WaitOne(0)
 if (-not $acquired) {throw 'Another JobRadar operations script is already running'}
 $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
 $reportRoot = Join-Path (Get-Location) 'reports'
 New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null
 Write-Host "JobRadar controlled run $stamp; no scheduler or Upwork API calls."

 if ($RunCrawler) {
  Write-Host 'Crawling configured employment sources once. Stop any Docker/scheduled collector FIRST.'
  & dotnet run --project $project -c Release -- --once
  $crawlCode = $LASTEXITCODE
  Write-Host "JobRadar crawl exit code: $crawlCode"
  if ($crawlCode -eq 3) {throw 'Cannot initialize PostgreSQL. Stop.'}
  if ($crawlCode -ne 0) {
   Write-Warning 'Partial crawl/source errors reported. Check crawl-*.json before interpreting coverage.'
  }
 }

 if (-not $SkipPending) {
  & dotnet run --project $project -c Release -- --pending-status
  if ($LASTEXITCODE -ne 0) {throw 'Pending status query failed'}
 }
 if (-not $SkipTriage) {
  & dotnet run --project $project -c Release -- --triage-jsonl
  if ($LASTEXITCODE -ne 0) {throw 'Triage export failed'}
 }
 if ($UpworkFile) {
  $path = (Resolve-Path -LiteralPath $UpworkFile -ErrorAction Stop).Path
  $hours = ((Get-Date).ToUniversalTime() -
   (Get-Item -LiteralPath $path).LastWriteTimeUtc).TotalHours
  if ($hours -gt 24) {throw 'Upwork file is older than 24 hours. Refresh it through the authorized connector.'}
  Write-Host 'Upwork: local, read-only, no Connects spent.'
  & dotnet run --project $project -c Release -- "--upwork-preview=$path"
  if ($LASTEXITCODE -ne 0) {throw 'Upwork snapshot has invalid or expired rows'}
  # Existing PostgreSQL jobs are read for unified view, but Upwork rows NEVER
  # enter the database. Keep all source output in the terminal.
  & dotnet run --project $project -c Release -- --opportunities-preview "--upwork-file=$path"
  if ($LASTEXITCODE -ne 0) {throw 'Unified opportunity preview failed'}
 }
 Write-Host 'Controlled JobRadar operation completed. See reports/ for persisted employment triage exports.'
}
finally {
 if ($acquired) {$mutex.ReleaseMutex()}
 $mutex.Dispose()
}
