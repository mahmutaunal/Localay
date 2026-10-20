# CI build tool only. This downloader is never shipped to end users.
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "The Inno Setup compiler requires Windows." }
$policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot "compiler.json") -Raw | ConvertFrom-Json
$tools = Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }) "Localay-Inno-$($policy.version)"
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$download = Join-Path $tools "compiler-setup.exe"
Invoke-WebRequest -Uri $policy.url -OutFile $download -MaximumRetryCount 3 -RetryIntervalSec 5
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $policy.sha256) {
    throw "The official compiler download does not match the pinned SHA-256."
}
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne $policy.publisher) {
    throw "The compiler download has an unexpected or invalid publisher signature."
}
$destination = Join-Path $tools "compiler"
$process = Start-Process -FilePath $download -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER',
    "/DIR=`"$destination`""
) -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
$compiler = Join-Path $destination "ISCC.exe"
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "ISCC.exe was not installed." }
& (Join-Path $PSScriptRoot 'Verify-Compiler.ps1') -CompilerPath $compiler
$env:LOCALAY_ISCC = $compiler
if ($env:GITHUB_ENV) { "LOCALAY_ISCC=$compiler" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8 }
Write-Host "Verified Inno Setup $($policy.version): $compiler"
