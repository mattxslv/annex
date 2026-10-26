# Functions shared by setup and the non-destructive regression checks.
function Initialize-SunshineConfiguration {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ComputerName)
    if (Test-Path -LiteralPath $Path) {
        Write-Host "Preserving existing Sunshine configuration: $Path"
        return
    }
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    $config = "sunshine_name = $ComputerName-Annex`r`nport = 48989`r`ncapture = wgc`r`n"
    [IO.File]::WriteAllText($Path, $config, (New-Object Text.UTF8Encoding($false)))
}

function Enable-AnnexSessions {
    param([Parameter(Mandatory)][string]$LauncherPath)
    # PowerShell does not reliably wait for a directly invoked GUI executable.
    $process = Start-Process -FilePath $LauncherPath -ArgumentList '-enable' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Failed to enable child sessions (exit code $($process.ExitCode))." }
}
