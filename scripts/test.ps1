<# Runs non-destructive checks. No admin rights, downloads, or Windows setting changes. #>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path ([IO.Path]::GetTempPath()) ('childstream-checks-' + [guid]::NewGuid().ToString('N') + '.exe')
$errors = @()
Get-ChildItem -LiteralPath (Join-Path $root 'scripts'), (Join-Path $root 'tests') -Filter '*.ps1' | ForEach-Object {
    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    $errors += $parseErrors
}
if ($errors.Count) { throw ($errors | Out-String) }
Write-Host 'PowerShell syntax checks passed'
try {
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
    & $csc /nologo /warnaserror /target:exe /platform:x64 /main:RegressionTests "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:Microsoft.CSharp.dll @sources (Join-Path $root 'tests\RegressionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Regression test compilation failed.' }
    & $output
    $csharpExitCode = $LASTEXITCODE
    # Isolate native-boundary test doubles from a caller's interactive session.
    $testShell = (Get-Process -Id $PID).Path
    & $testShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\scripts.tests.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell regression tests failed.' }
    if ($csharpExitCode -ne 0) { throw 'C# regression tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
}
