# Runs at every logon; skips the physical console. Ordinary RDP sessions also
# pass this legacy guard; see README limitations before using multi-user RDP.
# Starts a portable Sunshine instance located next to this repo (Sunshine\Sunshine\sunshine.exe).
$ErrorActionPreference = 'Stop'
Add-Type -Namespace ChildPOC -Name Native -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();'
$consoleSid = [ChildPOC.Native]::WTSGetActiveConsoleSessionId()
$mySid = (Get-Process -Id $PID).SessionId
if ($mySid -eq $consoleSid) { exit }

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Sunshine\Sunshine\sunshine.exe'
$running = Get-Process sunshine -ErrorAction SilentlyContinue | Where-Object SessionId -eq $mySid
if (-not $running -and (Test-Path $exe)) {
    try {
        Start-Process $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -Verb RunAs
        Add-Content (Join-Path $root 'autostart.log') "$(Get-Date -Format o) started sunshine in session $mySid (console=$consoleSid)"
    }
    catch {
        Add-Content (Join-Path $root 'autostart.log') "$(Get-Date -Format o) failed to start sunshine in session ${mySid}: $_"
        throw
    }
}

