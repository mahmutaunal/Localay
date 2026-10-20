param([Parameter(Mandatory = $true)][string]$PublishDirectory, [switch]$IncludeOneOcrAssets)
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Verify-Publish.ps1") -PublishDirectory $PublishDirectory -IncludeOneOcrAssets:$IncludeOneOcrAssets
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Localay.csproj")
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $version" }
$name = "Localay-$version-windows-x64"
$packageDirectory = Join-Path $PSScriptRoot "artifacts\package"
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$staging = Join-Path $packageDirectory $name
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $staging -Recurse -Force
foreach ($document in @("README.md", "LICENSE.md", "THIRD_PARTY_NOTICES.md", "SECURITY.md")) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $staging -Force
}
$zip = Join-Path $packageDirectory "$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -LiteralPath (Join-Path $packageDirectory "$name.sha256") -Encoding ascii
Write-Host "Package: $zip"
Write-Host "SHA-256: $hash"
