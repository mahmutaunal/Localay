param([switch]$IncludeOneOcrAssets, [string]$CompilerPath = $env:LOCALAY_ISCC)
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Run this release-validation script in PowerShell 7 on Windows. Cross-builds do not validate Windows behavior." }
$root = $PSScriptRoot
Push-Location $root
try {
    $publish = Join-Path $root "artifacts\publish"
    # Never mix output from older builds or locally saved settings into a new release.
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    & (Join-Path $root "Verify-SecurityPolicy.ps1")
    & (Join-Path $root "tests/InstallerPolicyChecks.ps1")
    dotnet restore .\Localay.csproj -r win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Restore or dependency security audit failed." }
    dotnet publish .\Localay.csproj -c Release -r win-x64 --self-contained true --no-restore -o $publish "-p:IncludeOneOcrAssets=$($IncludeOneOcrAssets.IsPresent.ToString().ToLowerInvariant())"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    & (Join-Path $root "Verify-Publish.ps1") -PublishDirectory $publish -IncludeOneOcrAssets:$IncludeOneOcrAssets
    if (-not $IncludeOneOcrAssets) {
        & (Join-Path $root "tests/PublishChecks.ps1") -PublishDirectory $publish
    }
    & (Join-Path $root "Scan-Windows.ps1") -PathToScan $publish -ReportName 'defender-payload.json'
    & (Join-Path $root "Package-Windows.ps1") -PublishDirectory $publish -IncludeOneOcrAssets:$IncludeOneOcrAssets
    & (Join-Path $root "Scan-Windows.ps1") -PathToScan (Join-Path $root "artifacts\package")
    & (Join-Path $root "Build-Installer.ps1") -PublishDirectory $publish -CompilerPath $CompilerPath -IncludeOneOcrAssets:$IncludeOneOcrAssets
    & (Join-Path $root "Scan-Windows.ps1") -PathToScan (Join-Path $root "artifacts\installer") -ReportName 'defender-installer.json'
    Write-Host "Localay Windows portable package and installer verified and scanned." -ForegroundColor Green
} finally { Pop-Location }
