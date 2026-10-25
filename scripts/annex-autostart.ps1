# Runs at every logon; only acts inside a child session (session != physical console).
# Starts a portable Sunshine instance located next to this repo (Sunshine\Sunshine\sunshine.exe).
Add-Type -Namespace Annex -Name Native -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();'
$consoleSid = [Annex.Native]::WTSGetActiveConsoleSessionId()
$mySid = (Get-Process -Id $PID).SessionId
if ($mySid -eq $consoleSid) { exit }

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Sunshine\Sunshine\sunshine.exe'
$running = Get-Process sunshine -ErrorAction SilentlyContinue | Where-Object SessionId -eq $mySid
if (-not $running -and (Test-Path $exe)) {
    Start-Process $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -Verb RunAs
    Add-Content (Join-Path $root 'autostart.log') "$(Get-Date -Format o) started sunshine in session $mySid (console=$consoleSid)"
}

# Optional per-machine additions (not tracked): autostart.local.ps1 in the repo root
$local = Join-Path $root 'autostart.local.ps1'
if (Test-Path $local) { & $local }
