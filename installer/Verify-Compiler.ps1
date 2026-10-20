[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$CompilerPath)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "Inno Setup compiler is missing: $CompilerPath"
}
$policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'compiler.json') -Raw | ConvertFrom-Json
# ISCC.exe's PE FileVersion is 0.0.0.0 in upstream sources. Compile an
# output-disabled script to verify the loaded engine, not that placeholder.
$probe = @'
[Setup]
AppName=Localay Compiler Check
AppVersion=1.0.0
DefaultDirName={tmp}\Localay-Compiler-Check
Uninstallable=no
CreateAppDir=no
Output=no
'@
$compilerOutput = @($probe | & $CompilerPath /O- - 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler check failed (exit $LASTEXITCODE):`n$($compilerOutput -join "`n")"
}
$match = [regex]::Match(($compilerOutput -join "`n"),
    '(?m)^Compiler engine version: .*? (?<Version>\d+\.\d+\.\d+)(?:\s|$)')
if (-not $match.Success -or $match.Groups['Version'].Value -ne $policy.version) {
    throw "Expected Inno Setup engine $($policy.version). Compiler output:`n$($compilerOutput -join "`n")"
}
Write-Host "Verified Inno Setup engine $($policy.version): $CompilerPath"
