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
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('annex-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Test-Case 'first setup writes a usable UTF-8 Sunshine config' {
        $config = Join-Path $fixture 'config\sunshine.conf'
        Initialize-SunshineConfiguration -Path $config -ComputerName 'TestPC'
        $text = [IO.File]::ReadAllText($config)
        Assert-True ($text -match 'sunshine_name = TestPC-Annex') 'missing host name'
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
        try { Enable-AnnexSessions -LauncherPath 'fixture.exe' }
        catch { $caught = $_.Exception.Message -match 'exit code 5' }
        Assert-True $caught 'native enable failure was not propagated'
    }
    Test-Case 'setup records each original Windows setting once, including ones added later' {
        $copy = Join-Path $fixture 'state project'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'scripts\setup.ps1') -Destination (Join-Path $copy 'scripts\setup.ps1')
        [IO.File]::WriteAllText((Join-Path $copy 'Annex.exe'), 'not executed')
        $state = Join-Path $copy 'setup-state.json'
        Set-Content -LiteralPath $state -Value '{ "fDenyTSConnections": 1, "RemoteDesktopRulesEnabled": [ "RemoteDesktop-UserMode-In-TCP" ] }'
        function Start-Process { param($FilePath, $ArgumentList, $WorkingDirectory, [switch]$Wait, [switch]$PassThru, $WindowStyle, $Verb) return [pscustomobject]@{ ExitCode = 9 } }
        $caught = $false
        try { & (Join-Path $copy 'scripts\setup.ps1') -NoBuild -NoShortcut *> $null } catch { $caught = $_.Exception.Message -match 'exit code 9' }
        Assert-True $caught 'stubbed desktop setup failure not reported'
        $recorded = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
        $names = $recorded.PSObject.Properties.Name
        Assert-True ($recorded.fDenyTSConnections -eq 1) 'an already recorded original was overwritten'
        Assert-True (@($recorded.RemoteDesktopRulesEnabled) -contains 'RemoteDesktop-UserMode-In-TCP') 'recorded firewall rules lost'
        foreach ($name in 'DWMFRAMEINTERVAL', 'SuppressWhenMinimized', 'ChildSessions', 'bEnumerateHWBeforeSW') { Assert-True ($names -contains $name) "$name not recorded" }
    }
    Test-Case 'uninstall restores every recorded setting even when one of them fails' {
        $copy = Join-Path $fixture 'uninstall project'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'scripts\setup-admin.ps1') -Destination (Join-Path $copy 'scripts\setup-admin.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'setup-state.json') -Value '{ "fDenyTSConnections": 1, "DWMFRAMEINTERVAL": null, "SuppressWhenMinimized": 2, "ChildSessions": false, "RemoteDesktopRulesEnabled": [ "RemoteDesktop-UserMode-In-TCP", "NotAnRdpRule" ], "bEnumerateHWBeforeSW": 0 }'
        $calls = New-Object 'Collections.Generic.List[string]'
        function Set-ItemProperty { param($LiteralPath, $Name, $Value, $Type) if ($Name -eq 'fDenyTSConnections') { throw 'simulated registry failure' }; $calls.Add("set $Name=$Value") }
        function Remove-ItemProperty { param($Path, $Name, $ErrorAction) $calls.Add("remove $Name") }
        function New-Item { param($Path, [switch]$Force) $calls.Add("create $Path") }
        function Get-NetFirewallRule { param($Group, $DisplayName, $ErrorAction) if ($Group) { [pscustomobject]@{ Name = 'RemoteDesktop-UserMode-In-TCP' } } }
        function Enable-NetFirewallRule { param($Name, $ErrorAction) $calls.Add("enable $Name") }
        function Add-Type { param($Namespace, $Name, $MemberDefinition)
            if (-not ('AnnexUninstall.Wts' -as [type])) { Microsoft.PowerShell.Utility\Add-Type -Namespace AnnexUninstall -Name Wts -MemberDefinition 'public static string Calls = ""; public static bool WTSEnableChildSessions(bool enable) { Calls += enable; return true; }' } }
        & (Join-Path $copy 'scripts\setup-admin.ps1') -Uninstall *> $null
        Assert-True ($LASTEXITCODE -eq 1) 'a failed restore was reported as success'
        foreach ($expected in 'remove DWMFRAMEINTERVAL', 'set RemoteDesktop_SuppressWhenMinimized=2', 'set bEnumerateHWBeforeSW=0', 'enable RemoteDesktop-UserMode-In-TCP') { Assert-True ($calls -contains $expected) "skipped after the failure: $expected" }
        Assert-True ($calls -notcontains 'enable NotAnRdpRule') 'enabled a rule outside the Remote Desktop group'
        Assert-True ([AnnexUninstall.Wts]::Calls -eq 'False') 'child sessions not restored'
    }
    Test-Case 'uninstall removes only this Annex copy''s audio routing' {
        $store = 'HKCU:\Software\AnnexTests\' + [guid]::NewGuid().ToString('N')
        try {
            $launcher = Join-Path $fixture 'Annex install\Annex.exe'
            $tail = $launcher.Substring(2)
            foreach ($entry in @(@('mine', "\Device\HarddiskVolume3$tail"), @('other copy', '\Device\HarddiskVolume3\Other\Annex.exe'), @('other app', "\Device\HarddiskVolume3$tail.bak"), @('no path', $null))) {
                $key = New-Item -Path (Join-Path $store $entry[0]) -Force
                if ($entry[1]) { Set-ItemProperty -LiteralPath $key.PSPath -Name '(default)' -Value $entry[1]; Set-ItemProperty -LiteralPath $key.PSPath -Name '000_000' -Value 'device' }
            }
            $removed = Remove-AnnexAudioRouting -Launcher $launcher -Store $store
            $left = @(Get-ChildItem -LiteralPath $store | ForEach-Object PSChildName)
            Assert-True ($removed -eq 1 -and $left -notcontains 'mine') 'this copy''s routing was not removed'
            Assert-True ($left.Count -eq 3) 'routing of other programs was removed'
            Assert-True ((Remove-AnnexAudioRouting -Launcher $launcher -Store "$store-missing") -eq 0) 'missing store not tolerated'
        } finally { Remove-Item -LiteralPath (Split-Path $store) -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Test-Case 'build rejects compiler errors and does not replace existing launcher' {
        $build = Join-Path $root 'scripts\build.ps1'
        Assert-True (Test-Path -LiteralPath $build) 'non-admin build script is missing'
        $copy = Join-Path $fixture 'broken project'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts'), (Join-Path $copy 'src') | Out-Null
        Copy-Item -LiteralPath $build -Destination (Join-Path $copy 'scripts\build.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'src\Broken.cs') -Value 'this is not valid C#;'
        $output = Join-Path $copy 'Annex.exe'
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
        Copy-Item -LiteralPath (Join-Path $root 'scripts\annex-autostart.ps1') -Destination (Join-Path $copy 'scripts\annex-autostart.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'Sunshine\Sunshine\sunshine.exe') -Value 'never executed'
        Set-Content -LiteralPath (Join-Path $copy 'Annex.exe') -Value 'never executed'
        # Only the native/process boundaries are replaced. The original startup script runs.
        function Start-Process {
            [CmdletBinding()]param($FilePath, $WorkingDirectory, $WindowStyle, $Verb)
            throw 'Elevation was canceled'
        }
        $caught = $false
        try { & (Join-Path $copy 'scripts\annex-autostart.ps1') }
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
        Copy-Item -LiteralPath (Join-Path $root 'scripts\annex-autostart.ps1') -Destination (Join-Path $copy 'scripts\annex-autostart.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'Annex.exe') -Value 'never executed'
        Set-Content -LiteralPath (Join-Path $copy 'Sunshine\Sunshine\sunshine.exe') -Value 'never executed'
        $helperLaunches = New-Object 'Collections.Generic.List[object]'
        function Start-Process {
            [CmdletBinding()]param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, $Verb)
            $helperLaunches.Add([pscustomobject]@{ Path = $FilePath; Args = $ArgumentList; Verb = $Verb; Window = $WindowStyle })
        }
        & (Join-Path $copy 'scripts\annex-autostart.ps1')
        Assert-True ($helperLaunches.Count -eq 1 -and $helperLaunches[0].Path -eq (Join-Path $copy 'Annex.exe')) 'startup launched a host instead of the helper'
        Assert-True ($helperLaunches[0].Args -eq '-agent') 'wrong helper arguments'
        Assert-True (-not $helperLaunches[0].Verb -and $helperLaunches[0].Window -eq 'Hidden') 'helper requested elevation or a visible window'
    }
    Test-Case 'setup wrapper uses EXE-contained setup without downloading a host' {
        $copy = Join-Path $fixture 'host optional setup'
        New-Item -ItemType Directory -Path (Join-Path $copy 'scripts') | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'scripts\setup.ps1') -Destination (Join-Path $copy 'scripts\setup.ps1')
        Copy-Item -LiteralPath $common -Destination (Join-Path $copy 'scripts\setup-common.ps1')
        Set-Content -LiteralPath (Join-Path $copy 'Annex.exe') -Value 'never executed'
        Set-Content -LiteralPath (Join-Path $copy 'scripts\build.ps1') -Value 'Write-Output "fixture build"'
        $setupCalls = New-Object 'Collections.Generic.List[object]'
        function Invoke-RestMethod { throw 'setup must not download a host' }
        function Invoke-WebRequest { throw 'setup must not download a host' }
        function Start-Process {
            [CmdletBinding()]param($FilePath, $ArgumentList, $WorkingDirectory, [switch]$Wait, [switch]$PassThru, $WindowStyle)
            $setupCalls.Add([pscustomobject]@{ Path = $FilePath; Args = $ArgumentList; Waited = [bool]$Wait; Returned = [bool]$PassThru })
            return [pscustomobject]@{ ExitCode = 5 }
        }
        $caught = $false
        try { & (Join-Path $copy 'scripts\setup.ps1') *> (Join-Path $fixture 'setup-wrapper.txt') }
        catch { $caught = $_.Exception.Message -match 'exit code 5' }
        Assert-True ($setupCalls.Count -eq 1 -and $setupCalls[0].Args -eq '-setup') 'wrapper did not dispatch EXE setup'
        Assert-True ($setupCalls[0].Waited -and $setupCalls[0].Returned -and $caught) 'setup failure was not awaited and reported'
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
