<#
.SYNOPSIS
  Administrator part of Annex setup (started by setup.ps1): firewall and Sunshine folder protection.
  -Uninstall (started by uninstall.ps1) undoes it and restores the settings in setup-state.json.
#>
param([switch]$KeepRemoteDesktop, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Start-Transcript -LiteralPath (Join-Path $root 'setup-admin.log') -Force | Out-Null
$sunshineDir = Join-Path $root 'Sunshine'
$sunshineExe = Join-Path $sunshineDir 'Sunshine\sunshine.exe'

# 'Annex Sunshine' rules that belong to this Annex folder (other Annex copies keep theirs)
function Get-OwnRules {
    Get-NetFirewallRule -DisplayName 'Annex Sunshine' -ErrorAction SilentlyContinue |
        Where-Object { $p = ($_ | Get-NetFirewallApplicationFilter).Program; $p -and $p.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) }
}

try {
    if ($Uninstall) {
        Get-OwnRules | Remove-NetFirewallRule
        if (Test-Path -LiteralPath $sunshineDir) {
            # Give the folder back to the user so the uninstaller can delete it.
            & takeown /F $sunshineDir /R /D Y | Out-Null
            & icacls $sunshineDir /reset /T /C /Q | Out-Null
            & icacls $sunshineDir /grant "${env:USERDOMAIN}\${env:USERNAME}:(OI)(CI)F" /T /C /Q | Out-Null
            Write-Host 'Sunshine folder unlocked'
        }
        $statePath = Join-Path $root 'setup-state.json'
        if (Test-Path -LiteralPath $statePath) {
            $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            $ts = 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server'
            if ($null -ne $state.fDenyTSConnections) { Set-ItemProperty $ts -Name fDenyTSConnections -Value ([int]$state.fDenyTSConnections) }
            if ($null -ne $state.DWMFRAMEINTERVAL) { Set-ItemProperty "$ts\WinStations" -Name DWMFRAMEINTERVAL -Value ([int]$state.DWMFRAMEINTERVAL) -Type DWord }
            else { Remove-ItemProperty "$ts\WinStations" -Name DWMFRAMEINTERVAL -ErrorAction SilentlyContinue }
            $client = 'HKCU:\Software\Microsoft\Terminal Server Client'
            if ($null -ne $state.SuppressWhenMinimized) { Set-ItemProperty $client -Name RemoteDesktop_SuppressWhenMinimized -Value ([int]$state.SuppressWhenMinimized) -Type DWord }
            else { Remove-ItemProperty $client -Name RemoteDesktop_SuppressWhenMinimized -ErrorAction SilentlyContinue }
            foreach ($name in @($state.RemoteDesktopRulesEnabled)) { if ($name) { Enable-NetFirewallRule -Name $name -ErrorAction SilentlyContinue } }
            Add-Type -Namespace AnnexUninstall -Name Wts -MemberDefinition '[DllImport("wtsapi32.dll")] public static extern bool WTSEnableChildSessions(bool enable);'
            [void][AnnexUninstall.Wts]::WTSEnableChildSessions([bool]$state.ChildSessions)
            Write-Host 'Windows settings restored to their state before Annex'
        } else { Write-Host 'No setup-state.json: Windows settings left as they are' }
        exit 0
    }

    # RDP stays loopback-only: loopback traffic bypasses the firewall, so disabling the
    # inbound Remote Desktop rules keeps RDP closed to the network.
    if (-not $KeepRemoteDesktop) {
        Get-NetFirewallRule -Group '@FirewallAPI.dll,-28752' -ErrorAction SilentlyContinue | Disable-NetFirewallRule
        Write-Host 'Remote Desktop inbound firewall rules disabled (RDP is loopback-only)'
    }

    if (Test-Path -LiteralPath $sunshineExe) {
        # A fresh portable Sunshine has no config yet, and once the folder is locked the helper (not
        # elevated) can't create one, so write the starting config first. An existing one is kept.
        . (Join-Path $PSScriptRoot 'setup-common.ps1')
        Initialize-SunshineConfiguration -Path (Join-Path $sunshineDir 'Sunshine\config\sunshine.conf') -ComputerName $env:COMPUTERNAME

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
        if (-not (Get-OwnRules | Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -eq $sunshineExe })) {
            New-NetFirewallRule -DisplayName 'Annex Sunshine' -Direction Inbound -Program $sunshineExe -Action Allow -Profile Any | Out-Null
        }
    }

    # Older all-users hooks from ChildStream / early Annex
    $common = [Environment]::GetFolderPath('CommonStartup')
    foreach ($old in 'childstream-sunshine.cmd', 'annex-sunshine.cmd') { Remove-Item -LiteralPath (Join-Path $common $old) -ErrorAction SilentlyContinue }
}
catch { Write-Error $_; Stop-Transcript | Out-Null; exit 1 }
finally { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null }
exit 0
