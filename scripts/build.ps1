<# Compiles the launcher without enabling child sessions or changing Windows settings. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw 'The 64-bit .NET Framework 4.x compiler is required.'
}
$output = Join-Path $root 'Annex.exe'
$temporary = Join-Path $root ('Annex-build-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    $compilerArgs = @('/nologo', '/warnaserror', '/target:winexe', '/platform:x64', "/out:$temporary",
        '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Security.dll', '/r:Microsoft.CSharp.dll', '/r:System.Web.Extensions.dll')
    $icon = Join-Path $root 'app.ico'
    if (Test-Path -LiteralPath $icon -PathType Leaf) { $compilerArgs += "/win32icon:$icon" }
    $manifest = Join-Path $root 'app.manifest'
    if (Test-Path -LiteralPath $manifest -PathType Leaf) { $compilerArgs += "/win32manifest:$manifest" }
    $compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
    & $csc @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw "Launcher compilation failed (exit code $LASTEXITCODE)." }
    Move-Item -LiteralPath $temporary -Destination $output -Force
    Write-Host 'Compiled Annex.exe'
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
