$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('annex-package-' + [guid]::NewGuid().ToString('N'))
$checks = 0
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') | Out-Null
try {
    $package = Join-Path $root 'scripts\package.ps1'
    Assert-True (Test-Path -LiteralPath $package) 'Release packaging script is missing'
    $manifest = Join-Path $root 'scripts\release-files.txt'
    Copy-Item -LiteralPath $package -Destination (Join-Path $fixture 'scripts\package.ps1')
    foreach ($relative in [IO.File]::ReadAllLines($manifest)) {
        if (-not $relative -or $relative.StartsWith('#')) { continue }
        $target = Join-Path $fixture $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target -Force
    }
    # Deliberately place harmless stand-ins at runtime and scratch paths.
    foreach ($relative in @('Annex.exe', 'cred.bin', 'launcher.log', 'display.cfg', '.git\config', '.superpowers\scratch.txt', '.impeccable\review\capture.png', 'Sunshine\Sunshine\sunshine.exe', 'src\scratch-secret.cs', 'tests\scratch.txt')) {
        $target = Join-Path $fixture $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($target, 'harmless fixture; never executed')
    }
    & (Join-Path $fixture 'scripts\package.ps1') -SkipBuild
    foreach ($name in @('Annex-portable.zip', 'Annex-source.zip')) {
        $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $fixture ('dist\' + $name)))
        try {
            $entries = @($zip.Entries | ForEach-Object FullName)
            foreach ($required in @('README.md', 'LICENSE', 'app.manifest', 'src/Annex.cs', 'src/SessionAgent.cs', 'scripts/setup.ps1', 'scripts/build.ps1')) {
                Assert-True ($entries -ccontains $required) "Missing $required in $name"
            }
            foreach ($forbidden in @('cred.bin', 'launcher.log', 'display.cfg', '.git/config', '.superpowers/scratch.txt', '.impeccable/review/capture.png', 'Sunshine/Sunshine/sunshine.exe', 'src/scratch-secret.cs', 'tests/scratch.txt')) {
                Assert-True ($entries -cnotcontains $forbidden) "Private/runtime file $forbidden leaked into $name"
            }
            Assert-True (($entries -ccontains 'Annex.exe') -eq ($name -eq 'Annex-portable.zip')) 'Binary included in wrong archive'
        }
        finally { $zip.Dispose() }
    }
    $checks++; Write-Host 'PASS release ZIPs include required files and exclude private/runtime/scratch data'
    $lines = [IO.File]::ReadAllLines((Join-Path $fixture 'dist\SHA256SUMS.txt'))
    Assert-True ($lines.Length -eq 3) 'Checksums must cover executable and both ZIPs'
    foreach ($line in $lines) {
        Assert-True ($line -match '^([A-Fa-f0-9]{64})  (Annex(?:\.exe|-portable\.zip|-source\.zip))$') 'Unexpected checksum entry'
        $expected = $Matches[1]; $fileName = $Matches[2]
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $fixture ('dist\' + $fileName)) -Algorithm SHA256).Hash -eq $expected) "Checksum mismatch for $fileName"
    }
    $checks++; Write-Host 'PASS release checksums match every delivered artifact'
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'dist\Annex-portable.zip')).Hash
    Remove-Item -LiteralPath (Join-Path $fixture 'README.md')
    $caught = $false
    try { & (Join-Path $fixture 'scripts\package.ps1') -SkipBuild *> (Join-Path $fixture 'expected-failure.txt') }
    catch { $caught = $_.Exception.Message -match 'README' }
    Assert-True $caught 'Incomplete source package was accepted'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $fixture 'dist\Annex-portable.zip')).Hash -eq $oldHash) 'Failed package replaced previous artifact'
    $checks++; Write-Host 'PASS missing release inputs fail before replacing existing artifacts'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if ((Split-Path $resolvedFixture -Parent) -eq [IO.Path]::GetTempPath().TrimEnd('\')) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
Write-Host "Packaging: $checks/3 passed"
