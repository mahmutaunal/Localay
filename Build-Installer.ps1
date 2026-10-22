[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$CompilerPath = $env:LOCALAY_ISCC,
    [switch]$IncludeOneOcrAssets
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Build the Windows installer on Windows (GitHub Actions is supported)." }
& (Join-Path $PSScriptRoot "Verify-InstallerPolicy.ps1")
& (Join-Path $PSScriptRoot "Verify-Publish.ps1") -PublishDirectory $PublishDirectory -IncludeOneOcrAssets:$IncludeOneOcrAssets
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "Set LOCALAY_ISCC to the verified Inno Setup compiler; CI runs installer/Install-Compiler.ps1."
}
$policy = Get-Content -LiteralPath (Join-Path $PSScriptRoot "installer/compiler.json") -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'installer/Verify-Compiler.ps1') -CompilerPath $CompilerPath
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Localay.csproj")
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $version" }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = Join-Path $PSScriptRoot "artifacts\installer"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null
& $CompilerPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" (Join-Path $PSScriptRoot "installer/Localay.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$installer = Join-Path $output "Localay-$version-Setup-x64.exe"
& (Join-Path $PSScriptRoot "Verify-Installer.ps1") -InstallerPath $installer
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
[pscustomobject]@{
    Version = $version; File = [IO.Path]::GetFileName($installer); SHA256 = $hash
    Compiler = $policy.version; CompilerDownloadSHA256 = $policy.sha256
    SourceCommit = $env:GITHUB_SHA; WorkflowRunId = $env:GITHUB_RUN_ID
    WorkflowRunAttempt = $env:GITHUB_RUN_ATTEMPT; RuntimeIdentifier = 'win-x64'
    InstallScope = 'machine'; PrivilegesRequired = 'admin'; StartMenu = 'automatic'
    TimestampUtc = [DateTime]::UtcNow.ToString('o'); Signed = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "build-info.json")
Write-Host "Built administrator-required installer: $installer"
