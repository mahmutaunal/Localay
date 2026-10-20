# The stock hosted image disables scans and excludes whole disks for performance.
# Re-enable protection only on the disposable GitHub-hosted VM, never an end user's PC.
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Defender preparation is restricted to disposable GitHub-hosted Windows runners.'
}
Start-Service -Name WinDefend
$preferences = Get-MpPreference
foreach ($kind in @('ExclusionPath', 'ExclusionExtension', 'ExclusionProcess')) {
    $values = @($preferences.$kind | Where-Object { $_ })
    if ($values.Count -gt 0) {
        $parameters = @{ $kind = $values; ErrorAction = 'Stop' }
        Remove-MpPreference @parameters
    }
}
# These switches ENABLE scanning; they never add an exclusion or disable protection.
Set-MpPreference -DisableArchiveScanning $false -DisableScriptScanning $false `
    -DisableIOAVProtection $false -DisableBehaviorMonitoring $false -DisableRealtimeMonitoring $false `
    -DisableBlockAtFirstSeen $false -ScanAvgCPULoadFactor 50
$status = Get-MpComputerStatus
$preferences = Get-MpPreference
if (-not $status.AntivirusEnabled -or -not $status.AMServiceEnabled -or
    $preferences.DisableArchiveScanning -or $preferences.ExclusionPath -or
    $preferences.ExclusionExtension -or $preferences.ExclusionProcess) {
    throw 'The hosted runner could not enable non-excluded Defender scans.'
}
New-Item -ItemType Directory -Force -Path artifacts | Out-Null
[pscustomobject]@{
    Purpose = 'Hosted-runner preparation, not a malware-scan result'
    AntivirusEnabled = $status.AntivirusEnabled
    ArchiveScanningEnabled = -not $preferences.DisableArchiveScanning
    Exclusions = 'removed from disposable hosted runner'
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -LiteralPath artifacts/defender-runner.json
