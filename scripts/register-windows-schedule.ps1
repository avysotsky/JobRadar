# PREPARE an opt-in Task Scheduler job. This file NEVER registers by default.
# Only -Register activates the Windows schedule for the CURRENT logged-on user.
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedHead,
 [switch]$Register,
 [switch]$Unregister,
 [string]$TaskName='JobRadar-Collector-Local'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
 throw 'This installer needs Windows Task Scheduler.'
}
if ($Register -and $Unregister) {throw 'Choose -Register OR -Unregister.'}
$repoRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runner=Join-Path $repoRoot 'scripts\run-windows-scheduled.ps1'
$identity=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$credentialFile=Join-Path $env:LOCALAPPDATA 'JobRadar\postgres-credential.xml'
Write-Host "Windows user: $identity"
Write-Host "Machine timezone: $((Get-TimeZone).Id) (triggers use MACHINE LOCAL time)"
Write-Host "Code pin: $ExpectedHead"
Write-Host 'Scheduled times: 08:00, 13:00, 19:00 (machine local, not necessarily Europe/Kyiv)'
if ($Unregister) {
 $existing=Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
 if ($existing) {
  Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
  Write-Host "Task removed: $TaskName. DPAPI credential not deleted."
 }
 else {Write-Host 'No task was registered.'}
 return
}
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {throw 'Scheduled runner script missing.'}
if (-not $Register) {
 Write-Host 'PREVIEW ONLY: no task created or modified.'
 Write-Host 'First validate this branch, use set-windows-credential.ps1, then re-run with -Register.'
 return
}
if (-not (Test-Path -LiteralPath $credentialFile -PathType Leaf)) {
 throw 'No encrypted DPAPI credential for the current Windows user.'
}
$head=(& git -C $repoRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $head.Trim() -ne $ExpectedHead) {
 throw 'Checkout HEAD differs from supplied pinned SHA.'
}
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
 throw "Existing task $TaskName. No overwrite; unregister it explicitly first."
}
$exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+
 $runner+'" -ExpectedHead '+$ExpectedHead
$action=New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $repoRoot
$triggers=@(
 (New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.AddHours(8))),
 (New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.AddHours(13))),
 (New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.AddHours(19)))
)
# DPAPI is user-profile bound. Interactive logon is deliberate; never use
# S4U, SYSTEM or another account that cannot decrypt the user's credential.
$principal=New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description 'Personal JobRadar vacancy discovery. No Upwork polling or automatic applications.' | Out-Null
Write-Host "REGISTERED: $TaskName (runs only while $identity is logged in)."
Write-Host "Check: Get-ScheduledTask -TaskName '$TaskName' | Get-ScheduledTaskInfo"
