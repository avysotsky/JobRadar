# Store only a Windows DPAPI encrypted user-scoped PSCredential, never plaintext.
# Run interactively as the SAME Windows account used by Task Scheduler.
[CmdletBinding()]
param([switch]$ReplaceExisting)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
 throw 'Windows DPAPI credential store is required.'
}
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
 throw 'LOCALAPPDATA is missing; no credential created.'
}
$directory = Join-Path $env:LOCALAPPDATA 'JobRadar'
$destination = Join-Path $directory 'postgres-credential.xml'
if ((Test-Path -LiteralPath $destination) -and -not $ReplaceExisting) {
 throw 'Encrypted credential already exists. Use -ReplaceExisting to rotate it.'
}
New-Item -Path $directory -ItemType Directory -Force | Out-Null
$secret = Read-Host 'Operational PostgreSQL password (input hidden)' -AsSecureString
$credential = [pscredential]::new('postgres',$secret)
$temporary = Join-Path $directory ([guid]::NewGuid().ToString('N') + '.tmp')
try {
 $credential | Export-Clixml -LiteralPath $temporary
 $roundtrip = Import-Clixml -LiteralPath $temporary
 if ($roundtrip -isnot [pscredential] -or $roundtrip.UserName -ne 'postgres') {
  throw 'DPAPI round-trip failed. Existing credential was not replaced.'
 }
 Move-Item -LiteralPath $temporary -Destination $destination -Force
 Write-Host "DPAPI credential stored for Windows user $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
 Write-Host "Credential file: $destination"
 Write-Host 'No PostgreSQL connection has been attempted. Never commit this file.'
}
finally {
 if (Test-Path -LiteralPath $temporary) {
  Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
 }
 Remove-Variable secret,credential,roundtrip -ErrorAction SilentlyContinue
}
