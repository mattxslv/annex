<# Creates release artifacts from an explicit source allowlist. Never runs setup. #>
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$manifest = Join-Path $PSScriptRoot 'release-files.txt'
$files = @()
foreach ($line in [IO.File]::ReadAllLines($manifest)) {
    $relative = $line.Trim().Replace('\', '/')
    if (-not $relative -or $relative.StartsWith('#')) { continue }
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Split('/') -contains '..') { throw "Invalid release path: $relative" }
    $source = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $source.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release input: $relative" }
    $item = Get-Item -LiteralPath $source
    while ($item.FullName -ne $root) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release input must not be a link: $relative" }
        $item = Get-Item -LiteralPath (Split-Path $item.FullName -Parent)
    }
    if ($files -contains $relative) { throw "Duplicate release input: $relative" }
    $files += $relative
}
if (-not $files.Count) { throw 'Release file allowlist is empty.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$executable = Join-Path $root 'ChildStream.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'ChildStream.exe is missing. Build before using -SkipBuild.' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$output = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$temporaryFiles = @()
try {
    $tag = [guid]::NewGuid().ToString('N')
    $artifacts = @('ChildStream.exe', 'ChildStream-portable.zip', 'ChildStream-source.zip')
    foreach ($name in $artifacts) {
        $temporary = Join-Path $output ($tag + '-' + $name)
        $temporaryFiles += $temporary
        if ($name -eq 'ChildStream.exe') { Copy-Item -LiteralPath $executable -Destination $temporary; continue }
        $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($relative in $files) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $relative), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
            if ($name -eq 'ChildStream-portable.zip') {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $executable, 'ChildStream.exe', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    $sums = @()
    for ($i = 0; $i -lt $artifacts.Count; $i++) {
        $sums += (Get-FileHash -LiteralPath $temporaryFiles[$i] -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $artifacts[$i]
    }
    $temporarySums = Join-Path $output ($tag + '-SHA256SUMS.txt')
    $temporaryFiles += $temporarySums
    [IO.File]::WriteAllLines($temporarySums, $sums, [Text.Encoding]::ASCII)
    for ($i = 0; $i -lt $artifacts.Count; $i++) { Move-Item -LiteralPath $temporaryFiles[$i] -Destination (Join-Path $output $artifacts[$i]) -Force }
    Move-Item -LiteralPath $temporarySums -Destination (Join-Path $output 'SHA256SUMS.txt') -Force
    Write-Host "Release executable, portable/source ZIPs and SHA256SUMS.txt written to $output"
}
finally {
    foreach ($temporary in $temporaryFiles) { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
