$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script:checks = 0
$script:failures = 0

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Test-Case([string]$Name, [scriptblock]$Test) {
    $script:checks++
    try { & $Test; Write-Host "PASS $Name" }
    catch { $script:failures++; Write-Host "FAIL ${Name}: $_" }
}

$common = Join-Path $root 'scripts\setup-common.ps1'
if (Test-Path -LiteralPath $common) { . $common }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('childstream-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Test-Case 'first setup writes a usable UTF-8 Sunshine config' {
        $config = Join-Path $fixture 'config\sunshine.conf'
        Initialize-SunshineConfiguration -Path $config -ComputerName 'TestPC'
        $text = [IO.File]::ReadAllText($config)
        Assert-True ($text -match 'sunshine_name = TestPC-Child') 'missing host name'
        Assert-True ($text -match 'port = 48989') 'missing isolated base port'
        Assert-True ($text -match 'capture = wgc') 'missing capture setting'
        Assert-True (-not $text.Contains([char]0)) 'config contains UTF-16 null bytes'
    }
    Test-Case 'rerunning setup preserves customized config byte for byte' {
        $config = Join-Path $fixture 'custom.conf'
        $original = "# custom`r`nport = 50000`r`nencoder = nvenc`r`n"
        [IO.File]::WriteAllText($config, $original)
        Initialize-SunshineConfiguration -Path $config -ComputerName 'OtherPC'
        Assert-True ([IO.File]::ReadAllText($config) -ceq $original) 'existing config was overwritten'
    }
    Test-Case 'setup waits for launcher and rejects native enable failure' {
        function Start-Process {
            param($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru, $WindowStyle)
            Assert-True ($Wait -and $PassThru) 'enable was not awaited'
            Assert-True ($ArgumentList -eq '-enable') 'wrong launcher command'
            return [pscustomobject]@{ ExitCode = 5 }
        }
        $caught = $false
        try { Enable-ChildStreamSessions -LauncherPath 'fixture.exe' }
        catch { $caught = $_.Exception.Message -match 'exit code 5' }
        Assert-True $caught 'native enable failure was not propagated'
    }
    Test-Case 'build rejects compiler errors and does not replace existing launcher' {
        $build = Join-Path $root 'scripts\build.ps1'
        Assert-True (Test-Path -LiteralPath $build) 'non-admin build script is missing'
        $copy = Join-Path $fixture 'broken project'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts'), (Join-Path $copy 'src') | Out-Null
        Copy-Item -LiteralPath $build -Destination (Join-Path $copy 'scripts\build.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'src\Broken.cs') -Value 'this is not valid C#;'
        $output = Join-Path $copy 'ChildStream.exe'
        [IO.File]::WriteAllText($output, 'previous working launcher')
        $caught = $false
        try { & (Join-Path $copy 'scripts\build.ps1') *> (Join-Path $fixture 'compiler-output.txt') }
        catch { $caught = $_.Exception.Message -match 'compil' }
        Assert-True $caught 'compiler failure did not stop the build'
        Assert-True ([IO.File]::ReadAllText($output) -eq 'previous working launcher') 'failed build replaced previous launcher'
    }
    Test-Case 'autostart records helper launch failure instead of success' {
        $copy = Join-Path $fixture 'autostart project'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts'), (Join-Path $copy 'Sunshine\Sunshine') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'scripts\childsession-autostart.ps1') -Destination (Join-Path $copy 'scripts\childsession-autostart.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'Sunshine\Sunshine\sunshine.exe') -Value 'never executed'
        Set-Content -LiteralPath (Join-Path $copy 'ChildStream.exe') -Value 'never executed'
        # Only the native/process boundaries are replaced. The original startup script runs.
        function Start-Process {
            [CmdletBinding()]param($FilePath, $WorkingDirectory, $WindowStyle, $Verb)
            throw 'Elevation was canceled'
        }
        $caught = $false
        try { & (Join-Path $copy 'scripts\childsession-autostart.ps1') }
        catch { $caught = $true }
        Assert-True $caught 'startup failure was hidden'
        $log = Join-Path $copy 'autostart.log'
        Assert-True (Test-Path -LiteralPath $log) 'startup failure was not logged'
        $text = [IO.File]::ReadAllText($log)
        Assert-True ($text -match 'failed' -and $text -notmatch 'started sunshine') 'log incorrectly reported success'
    }
    Test-Case 'startup launches only the idle session helper without elevation' {
        $copy = Join-Path $fixture 'helper startup'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts'), (Join-Path $copy 'Sunshine\Sunshine') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'scripts\childsession-autostart.ps1') -Destination (Join-Path $copy 'scripts\childsession-autostart.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'ChildStream.exe') -Value 'never executed'
        Set-Content -LiteralPath (Join-Path $copy 'Sunshine\Sunshine\sunshine.exe') -Value 'never executed'
        $helperLaunches = New-Object 'Collections.Generic.List[object]'
        function Start-Process {
            [CmdletBinding()]param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, $Verb)
            $helperLaunches.Add([pscustomobject]@{ Path = $FilePath; Args = $ArgumentList; Verb = $Verb; Window = $WindowStyle })
        }
        & (Join-Path $copy 'scripts\childsession-autostart.ps1')
        Assert-True ($helperLaunches.Count -eq 1 -and $helperLaunches[0].Path -eq (Join-Path $copy 'ChildStream.exe')) 'startup launched a host instead of the helper'
        Assert-True ($helperLaunches[0].Args -eq '-agent') 'wrong helper arguments'
        Assert-True (-not $helperLaunches[0].Verb -and $helperLaunches[0].Window -eq 'Hidden') 'helper requested elevation or a visible window'
    }
}
finally {
    # The path was created above under the OS temp directory with a random name.
    if ((Split-Path $fixture -Parent) -eq [IO.Path]::GetTempPath().TrimEnd('\')) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
Write-Host "PowerShell: $($script:checks - $script:failures)/$script:checks passed"
if ($script:failures) { throw "$script:failures PowerShell regression test(s) failed." }
