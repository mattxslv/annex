<#
.SYNOPSIS
  Downloads the latest portable Sunshine for the Annex desktop into .\Sunshine, verified against
  GitHub's SHA-256 digest, then protects the folder and adds its firewall rule (setup-admin.ps1).
.PARAMETER SkipAdmin
  Don't run the admin step (setup.ps1 runs it itself afterwards).
.PARAMETER Report
  Machine-readable progress for Annex's install window (STEP/PROGRESS/DONE/ERROR lines).
.NOTES
  Uses the portable "lite" zip, never the installer: the installer adds a system service that would
  collide with a main host such as Vibepollo/Apollo. An existing Sunshine folder and config are kept.
#>
[CmdletBinding()]
param([switch]$SkipAdmin, [switch]$Report)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # PowerShell's own progress block is noisy and slows downloads
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'Sunshine'
$exe = Join-Path $target 'Sunshine\sunshine.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) ('annex-sunshine-' + [guid]::NewGuid().ToString('N'))

function Step([string]$text) { if ($Report) { [Console]::Out.WriteLine("STEP $text"); [Console]::Out.Flush() } else { Write-Host "  $text" } }
function Ok([string]$text) { if ($Report) { [Console]::Out.WriteLine("OK $text"); [Console]::Out.Flush() } else { Write-Host '  ' -NoNewline; Write-Host ([char]0x2713) -ForegroundColor Green -NoNewline; Write-Host " $text" } }
function Progress([long]$done, [long]$total) {
    $pct = if ($total -gt 0) { [int](100 * $done / $total) } else { 0 }
    $mb = '{0:0.0} / {1:0.0} MB' -f ($done / 1MB), ($total / 1MB)
    if ($Report) { [Console]::Out.WriteLine("PROGRESS $pct $mb"); [Console]::Out.Flush(); return }
    $width = 28; $fill = [int]($width * $pct / 100)
    Write-Host ("`r  [" + ('#' * $fill) + ('-' * ($width - $fill)) + "] {0,3}%  $mb   " -f $pct) -NoNewline
}

function Download([string]$url, [string]$path, [long]$total) {
    $request = [Net.HttpWebRequest]::Create($url); $request.UserAgent = 'Annex'
    $response = $request.GetResponse()
    try {
        if ($response.ContentLength -gt 0) { $total = $response.ContentLength }
        $in = $response.GetResponseStream(); $out = [IO.File]::Create($path)
        try {
            $buffer = New-Object byte[] 262144; $done = 0L; $last = [DateTime]::MinValue
            while (($read = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $out.Write($buffer, 0, $read); $done += $read
                if (([DateTime]::UtcNow - $last).TotalMilliseconds -ge 150) { Progress $done $total; $last = [DateTime]::UtcNow }
            }
            Progress $done $total
            if (-not $Report) { Write-Host '' }
        } finally { $out.Dispose(); $in.Dispose() }
    } finally { $response.Close() }
}

$code = 0
try {
    if (-not $Report) { Write-Host ''; Write-Host '  Annex - install Sunshine' -ForegroundColor Cyan; Write-Host '' }
    if (Test-Path -LiteralPath $exe) {
        Ok "Sunshine is already installed in $target"
    } else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'AMD64' }
        $name = "Sunshine-Windows-$arch-lite.zip"
        Step 'Looking up the latest Sunshine release...'
        $release = Invoke-RestMethod 'https://api.github.com/repos/LizardByte/Sunshine/releases/latest' -Headers @{ 'User-Agent' = 'Annex' }
        $asset = $release.assets | Where-Object name -eq $name | Select-Object -First 1
        if (-not $asset) { throw "The latest release ($($release.tag_name)) has no $name." }
        if ($asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') { throw "GitHub did not publish a SHA-256 digest for $name; not installing an unverified download." }
        $expected = $Matches[1]
        Ok "Found Sunshine $($release.tag_name)"

        New-Item -ItemType Directory -Path $work | Out-Null
        $zip = Join-Path $work $name
        Step "Downloading Sunshine $($release.tag_name)..."
        Download $asset.browser_download_url $zip ([long]$asset.size)
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($actual -ne $expected.ToUpperInvariant()) { throw "Download checksum mismatch (expected $expected, got $actual). Nothing was installed." }
        Ok 'Downloaded and verified (SHA-256)'

        Step 'Unpacking...'
        $unpacked = Join-Path $work 'unpacked'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        $found = Get-ChildItem -LiteralPath $unpacked -Recurse -Filter sunshine.exe | Select-Object -First 1
        if (-not $found) { throw 'The download does not contain sunshine.exe.' }
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Move-Item -LiteralPath $found.Directory.FullName -Destination (Join-Path $target 'Sunshine')
        Ok "Installed to $target"
    }

    if (-not $SkipAdmin) {
        Step 'Protecting the Sunshine folder and adding its firewall rule (approve the Windows prompt)...'
        $admin = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\setup-admin.ps1`"")
        if ($admin.ExitCode -ne 0) { throw "The admin step failed (exit code $($admin.ExitCode)). See setup-admin.log." }
        Ok 'Folder protected, firewall rule added'
    }
    if ($Report) { [Console]::Out.WriteLine('DONE Sunshine is ready. Choose Start Sunshine in Annex.') } else { Write-Host ''; Write-Host '  Sunshine is ready.' -ForegroundColor Green }
}
catch {
    $code = 1
    $message = $_.Exception.Message
    if ($_.Exception -is [ComponentModel.Win32Exception] -and $_.Exception.NativeErrorCode -eq 1223) { $message = 'The Windows administrator prompt was declined. Sunshine is downloaded but not protected yet; run Install again to finish.' }
    if ($Report) { [Console]::Out.WriteLine("ERROR $message") } else { Write-Host ''; Write-Host "  Sunshine was not installed: $message" -ForegroundColor Red }
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
exit $code
