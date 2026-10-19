$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

dotnet restore .\Localay.csproj
dotnet publish .\Localay.csproj -c Release -r win-x64 --self-contained true

Write-Host "Localay publish completed." -ForegroundColor Green
Write-Host "Output: app\win-x64\publish" -ForegroundColor Cyan
