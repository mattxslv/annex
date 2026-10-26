<#
.SYNOPSIS
  Builds Annex.exe and the installer dist\AnnexSetup-<version>.exe (needs Inno Setup 6.6+).
.PARAMETER Version
  Version shown in the installer and Installed apps, e.g. 0.9.0.
#>
[CmdletBinding()]
param([string]$Version = '0.9.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup).' }
& $iscc /Q "/DAppVersion=$Version" (Join-Path $root 'installer\annex.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit code $LASTEXITCODE)." }
Get-Item (Join-Path $root "dist\AnnexSetup-$Version.exe") | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
