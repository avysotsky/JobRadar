# NON-MUTATING Windows PowerShell 5.1 syntax check for scheduling scripts.
# Does not create credentials, register tasks, query PostgreSQL or crawl sites.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$files=@(
 'scripts\set-windows-credential.ps1',
 'scripts\run-windows-scheduled.ps1',
 'scripts\register-windows-schedule.ps1'
)
foreach ($file in $files) {
 $path=Join-Path $root $file
 $tokens=$null
 $errors=$null
 [void][System.Management.Automation.Language.Parser]::ParseFile(
  $path,[ref]$tokens,[ref]$errors)
 if ($errors.Count -gt 0) {
  foreach ($parseError in $errors) {
   Write-Warning ($file+': '+$parseError.ToString())
  }
  throw "PowerShell parser failed: $file"
 }
 Write-Host "PowerShell syntax PASS: $file"
}
Write-Host 'Phase 20 script syntax PASS. No registration or provider HTTP calls performed.'
