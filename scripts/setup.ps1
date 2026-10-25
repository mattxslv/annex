<#
.SYNOPSIS
  Sets up Annex: builds Annex.exe, applies the Windows desktop settings and registers the child helper.
.PARAMETER KeepRemoteDesktop
  Leave the Windows "Remote Desktop" inbound firewall rules alone. By default they are
  disabled, because Annex only needs RDP over loopback.
.PARAMETER RouteAudio
  Download NirSoft svcl and route the viewer's audio to the silent Steam Streaming Speakers,
  so child-desktop sound plays only on the client.
.NOTES
  Run from PowerShell in the repo root:  .\scripts\setup.ps1
  Windows asks for administrator approval. Hosts are optional; an existing Sunshine config is kept.
#>
[CmdletBinding()]
param([switch]$KeepRemoteDesktop, [switch]$RouteAudio)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$launcher = Join-Path $root 'Annex.exe'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw 'Annex.exe is missing after build.' }
$process = Start-Process -FilePath $launcher -ArgumentList '-setup' -WorkingDirectory $root -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Desktop setup failed (exit code $($process.ExitCode))." }

# Firewall, Sunshine folder protection (admin)
$adminArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\setup-admin.ps1`"")
if ($KeepRemoteDesktop) { $adminArgs += '-KeepRemoteDesktop' }
$admin = Start-Process -FilePath 'powershell.exe' -ArgumentList $adminArgs -Verb RunAs -Wait -PassThru -WindowStyle Hidden
if ($admin.ExitCode -ne 0) { throw "Firewall/Sunshine protection step failed (exit code $($admin.ExitCode)). See setup-admin.log." }

# Child helper: a per-user Startup hook runs scripts\annex-autostart.ps1 at every logon.
# Written from here rather than by Annex.exe, so the executable never registers itself.
$startup = [Environment]::GetFolderPath('Startup')
foreach ($old in 'Annex helper.lnk', 'ChildStream helper.lnk') { Remove-Item -LiteralPath (Join-Path $startup $old) -ErrorAction SilentlyContinue }
Set-Content -LiteralPath (Join-Path $startup 'annex-helper.cmd') -Value "@powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSScriptRoot\annex-autostart.ps1`""
Write-Host 'Child helper registered (Startup\annex-helper.cmd)'

# Desktop shortcut
$ws = New-Object -ComObject WScript.Shell
$oldLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Child Session.lnk'
if ((Test-Path -LiteralPath $oldLnk) -and $ws.CreateShortcut($oldLnk).TargetPath -like '*\ChildStream.exe') { Remove-Item -LiteralPath $oldLnk }
$lnk = $ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Annex.lnk'))
$lnk.TargetPath = $launcher
$lnk.WorkingDirectory = $root
$lnk.Description = 'Second desktop for game streaming'
$lnk.Save()

if ($RouteAudio) {
    # Child-session sound is played on the host by the RDP viewer (Annex.exe), while the child's
    # host captures it from the child's "Remote Audio" endpoint. Route the viewer to the silent
    # "Steam Streaming Speakers" so audio plays only on the client. Other apps keep the default device.
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
}

Write-Host 'Annex setup complete. Launch "Annex" from the desktop. Install a streaming host separately when needed.'
