$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$publish = Join-Path $root "artifacts\publish"

dotnet restore .\Localay.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
dotnet publish .\Localay.csproj -c Release -r win-x64 --self-contained true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
& (Join-Path $root "Verify-Publish.ps1") -PublishDirectory $publish

Write-Host "Localay publish verified: $publish" -ForegroundColor Green
