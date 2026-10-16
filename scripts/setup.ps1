<#
.SYNOPSIS
  One-shot setup for ChildStream: second-desktop game streaming via Windows child sessions.
.NOTES
  Run from an elevated PowerShell in the repo root:  .\scripts\setup.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'setup-common.ps1')

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

# Prepare the launcher and dependency before changing Windows settings.
& (Join-Path $PSScriptRoot 'build.ps1')
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$startup = [Environment]::GetFolderPath('CommonStartup')
if (-not $desktop -or -not (Test-Path -LiteralPath $desktop -PathType Container)) {
    throw 'The current user desktop folder could not be found.'
}
if (-not $startup -or -not (Test-Path -LiteralPath $startup -PathType Container)) {
    throw 'The Windows common startup folder could not be found.'
}

# Download portable Sunshine if missing.
$sunshineExe = "$root\Sunshine\Sunshine\sunshine.exe"
if (-not (Test-Path -LiteralPath $sunshineExe -PathType Leaf)) {
    Write-Host 'Downloading Sunshine (portable lite)...'
    $rel = Invoke-RestMethod 'https://api.github.com/repos/LizardByte/Sunshine/releases/latest'
    $asset = @($rel.assets | Where-Object name -eq 'Sunshine-Windows-AMD64-lite.zip')
    if ($asset.Count -ne 1 -or -not $asset[0].browser_download_url) {
        throw 'The latest Sunshine release does not contain the expected AMD64 lite archive.'
    }
    $archive = Join-Path $root 'sunshine-lite.zip'
    try {
        Invoke-WebRequest $asset[0].browser_download_url -OutFile $archive -UseBasicParsing
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $root 'Sunshine') -Force
    }
    finally {
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    }
    if (-not (Test-Path -LiteralPath $sunshineExe -PathType Leaf)) {
        throw "Sunshine archive did not contain the expected executable: $sunshineExe"
    }
}

# Initial Sunshine config uses distinct ports so it can coexist with another host.
$confPath = Join-Path $root 'Sunshine\Sunshine\config\sunshine.conf'
Initialize-SunshineConfiguration -Path $confPath -ComputerName $env:COMPUTERNAME
Write-Host 'Sunshine config ready (new installs use base port 48989; existing settings are preserved).'

Enable-ChildStreamSessions -LauncherPath (Join-Path $root 'ChildStream.exe')
Write-Host 'Child sessions enabled'

# Allow loopback RDP and keep rendering while the viewer is minimized.
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations' -Name DWMFRAMEINTERVAL -Value 8 -Type DWord
New-Item -Path 'HKCU:\Software\Microsoft\Terminal Server Client' -Force | Out-Null
Set-ItemProperty 'HKCU:\Software\Microsoft\Terminal Server Client' -Name RemoteDesktop_SuppressWhenMinimized -Value 2 -Type DWord
Write-Host 'RDP composition and minimized rendering configured'

# Firewall rule.
if (-not (Get-NetFirewallRule -DisplayName 'ChildStream Sunshine' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'ChildStream Sunshine' -Direction Inbound -Program $sunshineExe -Action Allow -Profile Any | Out-Null
}

# Startup hook starts an idle helper; it never automatically starts a host.
$hook = Join-Path $startup 'childstream-sunshine.cmd'
$powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$startupScript = "& '" + (Join-Path $root 'scripts\childsession-autostart.ps1').Replace("'", "''") + "'"
# EncodedCommand is UTF-16LE by PowerShell contract and also handles CMD metacharacters in paths.
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($startupScript))
Set-Content -LiteralPath $hook -Value "@`"$powerShell`" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand $encoded" -Encoding ASCII

# Desktop shortcut.
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut((Join-Path $desktop 'Child Session.lnk'))
$lnk.TargetPath = "$root\ChildStream.exe"
$lnk.WorkingDirectory = $root
$lnk.Description = 'Second desktop for game streaming'
$lnk.Save()

Write-Host ''
Write-Host 'Done. Launch "Child Session" from the desktop, enter your Windows password once,'
Write-Host 'then configure controller input if needed and pair Moonlight/Artemis using the port in sunshine.conf (48989 by default).'
