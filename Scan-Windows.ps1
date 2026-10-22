param(
    [Parameter(Mandatory = $true)][string]$PathToScan,
    [ValidatePattern('^defender[-a-z]*\.json$')][string]$ReportName = 'defender-scan.json'
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows) { throw "Microsoft Defender validation requires Windows." }
$reports = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$reportPath = Join-Path $reports $ReportName
$scanLog = Join-Path $reports ([IO.Path]::GetFileNameWithoutExtension($ReportName) + '.log')
# Never let a previous successful report stand in for a failed scan attempt.
Remove-Item -LiteralPath $reportPath, $scanLog -Force -ErrorAction SilentlyContinue
$report = [ordered]@{
    Result = 'Not completed'
    RequestedPath = $PathToScan
    SourceCommit = $env:GITHUB_SHA
    WorkflowRunId = $env:GITHUB_RUN_ID
    WorkflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
}
try {
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
    $status = Get-MpComputerStatus
    $report.Engine = $status.AMEngineVersion
    $report.Signatures = $status.AntivirusSignatureVersion
    $scanRoot = (Resolve-Path -LiteralPath $PathToScan).Path
    $report.ScannedPath = $scanRoot
    $tool = Get-ChildItem -Path "$env:ProgramData\Microsoft\Windows Defender\Platform\*\MpCmdRun.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Name.Split('-')[0] } -Descending | Select-Object -First 1
    if ($tool) { $scanner = $tool.FullName }
    else { $scanner = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe' }
    if (-not (Test-Path -LiteralPath $scanner -PathType Leaf)) { throw "Microsoft Defender scanner was not found." }
    $before = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($scanRoot, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    if ($before.Count -eq 0) { throw "Refusing to report an empty directory as scanned." }
    $report.Files = $before
    # Scan-only mode reports detections without rewriting the release; it does not disable protection.
    # Handle the scanner's exit code ourselves, including when called from a native-error-aware shell.
    $PSNativeCommandUseErrorActionPreference = $false
    & $scanner -Scan -ScanType 3 -File $scanRoot -DisableRemediation 2>&1 | Tee-Object -FilePath $scanLog
    $report.ExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Defender scan failed or found a threat (exit $LASTEXITCODE). Do not distribute this package." }
    $status = Get-MpComputerStatus
    $report.Engine = $status.AMEngineVersion
    $report.Signatures = $status.AntivirusSignatureVersion
    $hashes = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($scanRoot, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    if ($before.Count -ne $hashes.Count -or (Compare-Object $before $hashes -Property Path, SHA256)) {
        throw "Release files changed or were quarantined during scanning; the scan is not valid for this artifact."
    }
    $report.Result = 'No threats reported by this scan'
    $report.Files = $hashes
} catch {
    $report.Result = 'Failed; release not validated'
    $report.Error = $_.Exception.Message
    throw
} finally {
    $report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath
}
