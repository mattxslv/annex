# Runs at logon. Sunshine starts only after a launcher confirmation.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$launcher = Join-Path $root 'Annex.exe'
try {
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw 'Annex.exe is missing. Run scripts\build.ps1 first.' }
    Start-Process -FilePath $launcher -ArgumentList '-agent' -WorkingDirectory $root -WindowStyle Hidden
    Add-Content -LiteralPath (Join-Path $root 'autostart.log') -Value "$(Get-Date -Format o) child helper launch requested"
}
catch {
    Add-Content -LiteralPath (Join-Path $root 'autostart.log') -Value "$(Get-Date -Format o) failed to launch child helper: $_"
    throw
}

