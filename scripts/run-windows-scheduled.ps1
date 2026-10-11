# One noninteractive, pinned-commit JobRadar Windows Task Scheduler execution.
# No Upwork polling, no automatic applications and no GitHub Actions.
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedHead,
 [string]$DbHost='127.0.0.1',
 [ValidateRange(1,65535)][int]$DbPort=54321,
 [string]$Database='jobradar_live',
 [string]$CredentialFile=(Join-Path $env:LOCALAPPDATA 'JobRadar\postgres-credential.xml'),
 [switch]$DryRun
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
 throw 'The scheduled runner requires Windows.'
}
$repoRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $repoRoot
if ($Database -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or
    $Database -match '(?i)test') {
 throw 'Refusing test or invalid operational database name.'
}
$head=(& git rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $head.Trim() -ne $ExpectedHead) {
 throw "Refusing unapproved checkout: actual HEAD $head does not match pinned SHA."
}
$dirty=@(& git status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
 throw 'Refusing tracked working tree modifications; schedule must run exactly the validated code.'
}
$project=Join-Path $repoRoot 'src\JobRadar\JobRadar.csproj'
if (-not (Test-Path -LiteralPath $project)) {throw 'JobRadar csproj missing.'}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
 throw 'dotnet SDK unavailable to the scheduled Windows user.'
}
if ($DryRun) {
 Write-Host "DRY RUN PASS: approved HEAD=$head; root=$repoRoot; database=$Database"
 Write-Host 'No credential read, no HTTP request, no PostgreSQL operation, no task registration.'
 return
}
if (-not (Test-Path -LiteralPath $CredentialFile -PathType Leaf)) {
 throw 'Windows DPAPI credential not found; run set-windows-credential.ps1 as the same Windows account.'
}
$credential=Import-Clixml -LiteralPath $CredentialFile
if ($credential -isnot [pscredential] -or $credential.UserName -ne 'postgres') {
 throw 'DPAPI credential invalid for current Windows identity.'
}
$logDir=Join-Path $env:LOCALAPPDATA 'JobRadar\logs'
New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$stamp=(Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$crawlLog=Join-Path $logDir "crawl-$stamp.log"
$triageLog=Join-Path $logDir "triage-$stamp.log"
# DbConnectionStringBuilder quotes separator/quote characters safely.
$db=[System.Data.Common.DbConnectionStringBuilder]::new()
$db['Host']=$DbHost
$db['Port']=$DbPort
$db['Database']=$Database
$db['Username']=$credential.UserName
$db['Password']=$credential.GetNetworkCredential().Password
$env:JOBRADAR_DB=$db.ConnectionString
try {
 Write-Host "JobRadar scheduled one-shot START. HEAD=$head; UTC=$stamp"
 Write-Host "Crawl log: $crawlLog"
 # Phase 19's DB-wide lease prevents two cooperating collectors from crawling
 # the same PostgreSQL database concurrently. Exit 5 means NO SCAN.
 & dotnet run --project $project -c Release -- --once *> $crawlLog
 $code=$LASTEXITCODE
 Write-Host "Collector exit=$code (0=complete, 4=degraded, 5=busy, 3=DB unavailable)."
 if ($code -eq 0 -or $code -eq 4) {
  # A degraded crawl still persists data. Export the accumulated actionable
  # triage reports after the source loop without repeating HTTP requests.
  & dotnet run --project $project -c Release -- --triage-jsonl *> $triageLog
  $triageCode=$LASTEXITCODE
  Write-Host "Triage export exit=$triageCode; log=$triageLog"
  if ($triageCode -ne 0) {exit 6}
 }
 if ($code -eq 4) {
  Write-Warning 'DEGRADED provider coverage; stored results were exported. Review crawl log.'
 }
 if ($code -eq 5) {
  Write-Warning 'Another JobRadar collector owns the database lease; NO NEW SCAN was performed.'
 }
 # Bound only these local diagnostic logs; do not delete audit exports.
 Get-ChildItem -LiteralPath $logDir -Filter '*.log' -File |
  Where-Object { $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-30) } |
  Remove-Item -Force -ErrorAction SilentlyContinue
 exit $code
}
finally {
 Remove-Item Env:JOBRADAR_DB -ErrorAction SilentlyContinue
 Remove-Variable credential,db -ErrorAction SilentlyContinue
}
