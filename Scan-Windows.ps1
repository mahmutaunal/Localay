param(
    [Parameter(Mandatory = $true)][string]$PathToScan,
    [ValidatePattern('^defender[-a-z]*\.json$')][string]$ReportName = 'defender-scan.json'
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Microsoft Defender validation requires Windows." }
$status = Get-MpComputerStatus
if (-not $status.AntivirusEnabled -or -not $status.AMServiceEnabled) {
    throw "Microsoft Defender Antivirus is unavailable or disabled; the release is not validated."
}
$preferences = Get-MpPreference
if ($preferences.DisableArchiveScanning) { throw "Defender archive scanning is disabled; package scan cannot be validated." }
if ($env:GITHUB_ACTIONS -eq 'true' -and ($preferences.ExclusionPath -or $preferences.ExclusionProcess -or $preferences.ExclusionExtension)) {
    throw "Hosted runner exclusions could invalidate the scan; run the Defender preparation step first."
}
Update-MpSignature
$scanRoot = (Resolve-Path -LiteralPath $PathToScan).Path
$tool = Get-ChildItem -Path "$env:ProgramData\Microsoft\Windows Defender\Platform\*\MpCmdRun.exe" |
    Sort-Object { [version]$_.Directory.Name.Split('-')[0] } -Descending | Select-Object -First 1
if (-not $tool) { throw "Microsoft Defender scanner was not found." }
$before = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($scanRoot, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
if ($before.Count -eq 0) { throw "Refusing to report an empty directory as scanned." }
# Scan-only mode reports detections without rewriting the release; this does NOT disable protection.
& $tool.FullName -Scan -ScanType 3 -File $scanRoot -DisableRemediation
if ($LASTEXITCODE -ne 0) { throw "Defender scan failed or found a threat (exit $LASTEXITCODE). Do not distribute this package." }
$status = Get-MpComputerStatus
$hashes = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($scanRoot, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
if ($before.Count -ne $hashes.Count -or (Compare-Object $before $hashes -Property Path, SHA256)) {
    throw "Release files changed or were quarantined during scanning; the scan is not valid for this artifact."
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'artifacts') | Out-Null
[pscustomobject]@{
    Result = "No threats reported by this scan"
    ScannedPath = $scanRoot
    TimestampUtc = [DateTime]::UtcNow.ToString("o")
    Engine = $status.AMEngineVersion
    Signatures = $status.AntivirusSignatureVersion
    Files = $hashes
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "artifacts\$ReportName")
