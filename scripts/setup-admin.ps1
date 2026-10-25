<#
.SYNOPSIS
  Administrator part of Annex setup (started by setup.ps1): firewall and Sunshine folder protection.
#>
param([switch]$KeepRemoteDesktop)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Start-Transcript -LiteralPath (Join-Path $root 'setup-admin.log') -Force | Out-Null
try {
    # RDP stays loopback-only: loopback traffic bypasses the firewall, so disabling the
    # inbound Remote Desktop rules keeps RDP closed to the network.
    if (-not $KeepRemoteDesktop) {
        Get-NetFirewallRule -Group '@FirewallAPI.dll,-28752' -ErrorAction SilentlyContinue | Disable-NetFirewallRule
        Write-Host 'Remote Desktop inbound firewall rules disabled (RDP is loopback-only)'
    }

    $sunshineDir = Join-Path $root 'Sunshine'
    $sunshineExe = Join-Path $sunshineDir 'Sunshine\sunshine.exe'
    if (Test-Path -LiteralPath $sunshineExe) {
        # The child host runs elevated only when users cannot change its files (see ElevationPolicy.cs).
        # Owner becomes Administrators so the user can't silently grant themselves write access back.
        & icacls $sunshineDir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Taking ownership of the Sunshine folder failed.' }
        & icacls $sunshineDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${env:USERDOMAIN}\${env:USERNAME}:(OI)(CI)RX" /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Locking down the Sunshine folder failed.' }
        & icacls "$sunshineDir\*" /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Resetting Sunshine file permissions failed.' }
        Write-Host 'Sunshine folder is admin-write only'

        Get-NetFirewallRule -DisplayName 'ChildStream Sunshine' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        if (-not (Get-NetFirewallRule -DisplayName 'Annex Sunshine' -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName 'Annex Sunshine' -Direction Inbound -Program $sunshineExe -Action Allow -Profile Any | Out-Null
        }
    }

    # Older all-users hooks from ChildStream / early Annex
    $common = [Environment]::GetFolderPath('CommonStartup')
    foreach ($old in 'childstream-sunshine.cmd', 'annex-sunshine.cmd') { Remove-Item -LiteralPath (Join-Path $common $old) -ErrorAction SilentlyContinue }
}
catch { Write-Error $_; Stop-Transcript | Out-Null; exit 1 }
Stop-Transcript | Out-Null
exit 0
