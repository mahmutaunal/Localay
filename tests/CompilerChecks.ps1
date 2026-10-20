$ErrorActionPreference = 'Stop'
$verify = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer/Verify-Compiler.ps1'
$policy = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'installer/compiler.json') -Raw | ConvertFrom-Json
$temporary = Join-Path ([IO.Path]::GetTempPath()) "localay-compiler-check-$([guid]::NewGuid()).ps1"
$previousExitCode = $global:LASTEXITCODE
try {
    # Stand in for the native compiler; no Windows executable runs in this test.
    $cases = @(
        @{ Version = $policy.version; ExitCode = 0; Accept = $true },
        @{ Version = "$($policy.version)0"; ExitCode = 0; Accept = $false },
        @{ Version = '0.0.0'; ExitCode = 0; Accept = $false },
        @{ Version = ''; ExitCode = 0; Accept = $false },
        @{ Version = $policy.version; ExitCode = 2; Accept = $false }
    )
    foreach ($case in $cases) {
        @"
if (`$args.Count -ne 2 -or `$args[0] -ne '/O-' -or `$args[1] -ne '-') { throw 'Expected output-disabled stdin compilation.' }
`$probe = @(`$input) -join "``n"
if (`$probe -notmatch '(?m)^Output=no$') { throw 'Probe must not generate an installer.' }
'Compiler engine version: Inno Setup $($case.Version)'
`$global:LASTEXITCODE = $($case.ExitCode)
"@ | Set-Content -LiteralPath $temporary -Encoding utf8
        $accepted = $true
        try { & $verify -CompilerPath $temporary } catch { $accepted = $false }
        if ($accepted -ne $case.Accept) {
            throw "Unexpected compiler verification result: version='$($case.Version)', exit=$($case.ExitCode), accepted=$accepted"
        }
    }
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $global:LASTEXITCODE = $previousExitCode
}
Write-Host 'Compiler checks passed: pinned engine accepted; prefix collision, wrong/missing version and failed compilation rejected.'
