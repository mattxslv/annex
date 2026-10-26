<#
.SYNOPSIS
  Removes what Annex setup added: signs out the Annex desktop, removes the helper hook, shortcut,
  firewall rule and user data, unlocks the Sunshine folder, and restores the Windows settings that
  setup recorded in setup-state.json. Used by the installer's uninstaller; also runnable by hand.
.NOTES
  powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
  One administrator prompt. Files in the Annex folder are left for the uninstaller (or you) to delete.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$me = (Get-Process -Id $PID).SessionId

# Sign out this user's Annex desktop (a child session) and close Annex, so no file stays in use.
foreach ($line in (query session 2>$null | Select-String "^\s*\S*\s+$env:USERNAME\s+(\d+)\s")) {
    $id = [int]$line.Matches[0].Groups[1].Value
    if ($id -ne $me) { Write-Host "Signing out the Annex desktop (session $id)"; logoff $id }
}
Get-Process Annex -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
Start-Sleep -Seconds 2

$startup = [Environment]::GetFolderPath('Startup')
$desktop = [Environment]::GetFolderPath('Desktop')
foreach ($item in (Join-Path $startup 'annex-helper.cmd'), (Join-Path $desktop 'Annex.lnk'), (Join-Path $env:LOCALAPPDATA 'Annex')) {
    if (Test-Path -LiteralPath $item) { Remove-Item -LiteralPath $item -Recurse -Force -ErrorAction SilentlyContinue }
}

$admin = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\setup-admin.ps1`"", '-Uninstall')
if ($admin.ExitCode -ne 0) { Write-Warning "The administrator part did not finish (exit $($admin.ExitCode)). See setup-admin.log." }
Write-Host 'Annex setup removed.'
