<# Development wrapper. The EXE contains Windows desktop setup; hosts are optional. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$launcher = Join-Path $root 'ChildStream.exe'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw 'ChildStream.exe is missing after build.' }
$process = Start-Process -FilePath $launcher -ArgumentList '-setup' -WorkingDirectory $root -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Desktop setup failed (exit code $($process.ExitCode))." }
Write-Host 'Windows desktop setup complete. Install a streaming host separately when needed.'
