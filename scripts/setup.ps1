<#
.SYNOPSIS
  One-shot setup for Annex: second-desktop game streaming via Windows child sessions.
.PARAMETER KeepRemoteDesktop
  Leave the Windows "Remote Desktop" inbound firewall rules alone. By default they are
  disabled, because Annex only needs RDP over loopback.
.NOTES
  Run from an elevated PowerShell in the repo root:  .\scripts\setup.ps1
  Safe to re-run; an existing Sunshine config is kept.
#>
param([switch]$KeepRemoteDesktop)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

# 1. Compile the launcher
$csc = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$iconArg = if (Test-Path "$root\app.ico") { "/win32icon:$root\app.ico" } else { $null }
& $csc /nologo /target:winexe $iconArg /out:"$root\Annex.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:Microsoft.CSharp.dll "$root\src\Annex.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compiling Annex.exe failed.' }
Write-Host 'Compiled Annex.exe'

# 2. Enable child sessions
& "$root\Annex.exe" -enable
if ($LASTEXITCODE -ne 0) { throw 'Failed to enable child sessions.' }
Write-Host 'Child sessions enabled'

# 3. Allow RDP connections (loopback requirement for child sessions), but keep RDP closed to the
#    network: loopback traffic bypasses the firewall, so disabling the inbound rules is enough.
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
if (-not $KeepRemoteDesktop) {
    Get-NetFirewallRule -Group '@FirewallAPI.dll,-28752' -ErrorAction SilentlyContinue | Disable-NetFirewallRule
    Write-Host 'RDP enabled for loopback only (Remote Desktop firewall rules disabled; use -KeepRemoteDesktop to skip)'
}

# 4. Raise session composition rate (~120 fps). Remove the value to restore default (~30 fps).
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations' -Name DWMFRAMEINTERVAL -Value 8 -Type DWord
Write-Host 'RDP composition rate raised'

# 4b. Keep rendering the session while the viewer window is minimized (prevents black stream)
New-Item -Path 'HKCU:\Software\Microsoft\Terminal Server Client' -Force | Out-Null
Set-ItemProperty 'HKCU:\Software\Microsoft\Terminal Server Client' -Name RemoteDesktop_SuppressWhenMinimized -Value 2 -Type DWord

# 5. Download portable Sunshine if missing
$sunshineDir = "$root\Sunshine"
$sunshineExe = "$sunshineDir\Sunshine\sunshine.exe"
if (-not (Test-Path $sunshineExe)) {
    Write-Host 'Downloading Sunshine (portable lite)...'
    $rel = Invoke-RestMethod 'https://api.github.com/repos/LizardByte/Sunshine/releases/latest'
    $asset = $rel.assets | Where-Object name -eq 'Sunshine-Windows-AMD64-lite.zip'
    Invoke-WebRequest $asset.browser_download_url -OutFile "$root\sunshine-lite.zip"
    Expand-Archive "$root\sunshine-lite.zip" $sunshineDir -Force
    Remove-Item "$root\sunshine-lite.zip"
}

# 6. Sunshine config: distinct name + ports so it can coexist with another host (e.g. Apollo/Vibepollo on 47989)
$confDir = "$sunshineDir\Sunshine\config"
New-Item -ItemType Directory -Path $confDir -Force | Out-Null
if (-not (Test-Path "$confDir\sunshine.conf")) {
    @"
sunshine_name = $env:COMPUTERNAME-Annex
port = 48989
capture = wgc
"@ | Set-Content "$confDir\sunshine.conf"
    Write-Host 'Sunshine configured (base port 48989). Set web UI credentials with: sunshine.exe --creds <user> <pass>'
} else {
    Write-Host 'Keeping existing Sunshine config'
}

# 6b. Sunshine runs elevated, so only admins may change its files (exe, config, apps.json);
#     otherwise any program running as you could make it run commands as admin.
#     You keep read access; edit the config through the Sunshine web UI or an elevated shell.
#     Owner becomes Administrators so you can't silently grant yourself write access back.
& icacls $sunshineDir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Taking ownership of the Sunshine folder failed.' }
& icacls $sunshineDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${env:USERDOMAIN}\${env:USERNAME}:(OI)(CI)RX" /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Locking down the Sunshine folder failed.' }
& icacls "$sunshineDir\*" /reset /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Resetting Sunshine file permissions failed.' }
Write-Host 'Sunshine folder is now admin-write only'

# 7. Firewall rule
Get-NetFirewallRule -DisplayName 'ChildStream Sunshine' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if (-not (Get-NetFirewallRule -DisplayName 'Annex Sunshine' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Annex Sunshine' -Direction Inbound -Program $sunshineExe -Action Allow -Profile Any | Out-Null
}

# 8. Startup hook (all users) so Sunshine starts inside child sessions automatically
$startup = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup'
Remove-Item "$startup\childstream-sunshine.cmd" -ErrorAction SilentlyContinue
Set-Content "$startup\annex-sunshine.cmd" "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$root\scripts\annex-autostart.ps1`""

# 9. Desktop shortcut
$ws = New-Object -ComObject WScript.Shell
$oldLnk = "$env:USERPROFILE\Desktop\Child Session.lnk"
if ((Test-Path $oldLnk) -and $ws.CreateShortcut($oldLnk).TargetPath -eq "$root\ChildStream.exe") { Remove-Item $oldLnk }
$lnk = $ws.CreateShortcut("$env:USERPROFILE\Desktop\Annex.lnk")
$lnk.TargetPath = "$root\Annex.exe"
$lnk.WorkingDirectory = $root
$lnk.Description = 'Second desktop for game streaming'
$lnk.Save()

# 10. Audio: child-session sound is played on the host by the RDP viewer (Annex.exe),
#     while the child's Sunshine captures it from the child's "Remote Audio" endpoint.
#     Route the viewer to the silent "Steam Streaming Speakers" so audio plays only on the
#     client instead of the host speakers too. Native apps keep the normal default device.
$svcl = "$root\tools\svcl.exe"
if (-not (Test-Path $svcl)) {
    New-Item -ItemType Directory -Path "$root\tools" -Force | Out-Null
    Invoke-WebRequest 'https://www.nirsoft.net/utils/svcl-x64.zip' -OutFile "$root\tools\svcl.zip" -UseBasicParsing
    Expand-Archive "$root\tools\svcl.zip" "$root\tools" -Force
    Remove-Item "$root\tools\svcl.zip"
}
$csv = Join-Path $env:TEMP 'annex-audio.csv'
& $svcl /scomma $csv /Columns 'Type,Direction,Device Name'
$hasSteamSpeakers = (Test-Path $csv) -and (Import-Csv $csv | Where-Object { $_.Type -eq 'Device' -and $_.Direction -eq 'Render' -and $_.'Device Name' -eq 'Steam Streaming Speakers' })
Remove-Item $csv -ErrorAction SilentlyContinue
if ($hasSteamSpeakers) {
    & $svcl /SetAppDefault 'Steam Streaming Speakers\Device\Speakers\Render' all 'Annex.exe'
    Write-Host 'Annex audio routed to Steam Streaming Speakers (client only)'
} else {
    Write-Warning 'Steam Streaming Speakers not found (install Steam or the Sunshine Steam audio driver); Annex audio will also play on host speakers.'
}

Write-Host ''
Write-Host 'Done. Launch "Annex" from the desktop, enter your Windows password once,'
Write-Host 'then pair Moonlight/Artemis with <host-ip>:48989.'
