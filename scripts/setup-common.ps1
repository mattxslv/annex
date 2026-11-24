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

# setup.ps1 -RouteAudio gives this Annex.exe its own default output device. Windows keeps that per program
# path; svcl can only reset it while the program runs, so uninstall removes the stored entry directly.
function Remove-AnnexAudioRouting {
    param([Parameter(Mandatory)][string]$Launcher, [string]$Store = 'HKCU:\Software\Microsoft\Multimedia\Audio\DefaultEndpoint')
    if (-not (Test-Path -LiteralPath $Store)) { return 0 }
    # Entries name the program by device path (\Device\HarddiskVolumeN\...), so compare without the drive.
    $suffix = [IO.Path]::GetFullPath($Launcher).Substring(2)
    $removed = 0
    foreach ($entry in @(Get-ChildItem -LiteralPath $Store)) {
        $program = $entry.GetValue('')
        if ($program -is [string] -and $program.StartsWith('\Device\', [StringComparison]::OrdinalIgnoreCase) -and $program.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $entry.PSPath -Recurse -Force; $removed++
        }
    }
    return $removed
}
