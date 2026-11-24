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

# Sign out this user's Annex desktop (the child session Windows reports, never another session of this user)
# and close Annex, so no file stays in use.
try {
    Add-Type -Namespace AnnexUninstall -Name Child -MemberDefinition '[DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSGetChildSessionId(out uint sessionId);'
    $child = [uint32]0
    if ([AnnexUninstall.Child]::WTSGetChildSessionId([ref]$child) -and $child -ne 0 -and $child -ne [uint32]::MaxValue -and $child -ne $me) {
        Write-Host "Signing out the Annex desktop (session $child)"; logoff $child
    }
} catch { Write-Warning "Could not sign out the Annex desktop: $_" }
# Audio routing (setup -RouteAudio): svcl resets it only while this Annex.exe runs, so do it before closing it.
$svcl = Join-Path $root 'tools\svcl.exe'
if ((Test-Path -LiteralPath $svcl) -and (Get-Process Annex -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $root 'Annex.exe') })) {
    try { & $svcl /SetAppDefault DefaultRenderDevice all (Join-Path $root 'Annex.exe') } catch { Write-Warning "Could not reset the Annex audio routing: $_" }
}
Get-Process Annex -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root + '\',[StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
Start-Sleep -Seconds 2

try {
    . (Join-Path $PSScriptRoot 'setup-common.ps1')
    if ((Remove-AnnexAudioRouting -Launcher (Join-Path $root 'Annex.exe')) -gt 0) { Write-Host 'Annex audio routing removed' }
} catch { Write-Warning "Could not remove the Annex audio routing: $_" }

$startup = [Environment]::GetFolderPath('Startup')
$desktop = [Environment]::GetFolderPath('Desktop')
foreach ($item in (Join-Path $startup 'annex-helper.cmd'), (Join-Path $desktop 'Annex.lnk'), (Join-Path $env:LOCALAPPDATA 'Annex')) {
    if (Test-Path -LiteralPath $item) { Remove-Item -LiteralPath $item -Recurse -Force -ErrorAction SilentlyContinue }
}

$admin = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\setup-admin.ps1`"", '-Uninstall')
if ($admin.ExitCode -ne 0) { Write-Warning "The administrator part did not finish (exit $($admin.ExitCode)). See setup-admin.log." }
Write-Host 'Annex setup removed.'
