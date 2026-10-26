<#
.SYNOPSIS
  Downloads the latest portable Sunshine for the Annex desktop into .\Sunshine, verified against
  GitHub's SHA-256 digest, then protects the folder and adds its firewall rule (setup-admin.ps1).
.PARAMETER SkipAdmin
  Don't run the admin step (setup.ps1 runs it itself afterwards).
.PARAMETER Pause
  Wait for a key at the end (used when Annex opens this in its own window).
.NOTES
  Uses the portable "lite" zip, never the installer: the installer adds a system service that would
  collide with a main host such as Vibepollo/Apollo. An existing Sunshine folder and config are kept.
#>
[CmdletBinding()]
param([switch]$SkipAdmin, [switch]$Pause)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'Sunshine'
$exe = Join-Path $target 'Sunshine\sunshine.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) ('annex-sunshine-' + [guid]::NewGuid().ToString('N'))
$code = 0
try {
    if (Test-Path -LiteralPath $exe) {
        Write-Host "Sunshine is already in $target. Nothing to download."
    } else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'AMD64' }
        $name = "Sunshine-Windows-$arch-lite.zip"
        Write-Host 'Looking up the latest Sunshine release...'
        $release = Invoke-RestMethod 'https://api.github.com/repos/LizardByte/Sunshine/releases/latest' -Headers @{ 'User-Agent' = 'Annex' }
        $asset = $release.assets | Where-Object name -eq $name | Select-Object -First 1
        if (-not $asset) { throw "The latest release ($($release.tag_name)) has no $name." }
        if ($asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') { throw "GitHub did not publish a SHA-256 digest for $name; not installing an unverified download." }
        $expected = $Matches[1]

        New-Item -ItemType Directory -Path $work | Out-Null
        $zip = Join-Path $work $name
        Write-Host "Downloading Sunshine $($release.tag_name) ($([math]::Round($asset.size / 1MB)) MB)..."
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'Annex' }
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($actual -ne $expected.ToUpperInvariant()) { throw "Download checksum mismatch (expected $expected, got $actual). Nothing was installed." }
        Write-Host 'Checksum verified.'

        $unpacked = Join-Path $work 'unpacked'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        $found = Get-ChildItem -LiteralPath $unpacked -Recurse -Filter sunshine.exe | Select-Object -First 1
        if (-not $found) { throw 'The download does not contain sunshine.exe.' }
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Move-Item -LiteralPath $found.Directory.FullName -Destination (Join-Path $target 'Sunshine')
        Write-Host "Sunshine $($release.tag_name) installed to $target"
    }

    if (-not $SkipAdmin) {
        Write-Host 'Protecting the Sunshine folder and adding its firewall rule (approve the admin prompt)...'
        $admin = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\setup-admin.ps1`"")
        if ($admin.ExitCode -ne 0) { throw "The admin step failed (exit code $($admin.ExitCode)). See setup-admin.log." }
    }
    Write-Host "`nDone. In Annex, the host card now offers Start Sunshine."
}
catch { Write-Host "`nSunshine was not installed: $($_.Exception.Message)" -ForegroundColor Red; $code = 1 }
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    if ($Pause) { Write-Host 'Press any key to close this window.'; [void][Console]::ReadKey($true) }
}
exit $code
