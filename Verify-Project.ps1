param(
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'artifacts/validation/project'),
    [switch]$SkipRestore
)
$ErrorActionPreference = 'Stop'
$steps = [Collections.Generic.List[object]]::new()
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$buildSucceeded = $false

function Invoke-Check([string]$Name, [scriptblock]$Action) {
    $started = [DateTime]::UtcNow
    $log = Join-Path $ReportDirectory ($Name + '.log')
    try {
        & $Action *>&1 | Tee-Object -FilePath $log | ForEach-Object { Write-Host $_ }
        $steps.Add([pscustomobject]@{ name = $Name; status = 'passed'; log = $log; seconds = ([DateTime]::UtcNow - $started).TotalSeconds })
    } catch {
        $_ | Out-String | Add-Content -LiteralPath $log
        Write-Warning "$Name failed: $_"
        $steps.Add([pscustomobject]@{ name = $Name; status = 'failed'; log = $log; seconds = ([DateTime]::UtcNow - $started).TotalSeconds; error = $_.ToString() })
    }
}
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
}
function Invoke-ProjectCheck([string]$Project, [string[]]$ExtraArguments = @()) {
    $command = @('run', '--project', "./tests/$Project/$Project.csproj", '-c', 'Release')
    if ($SkipRestore) { $command += '--no-restore' }
    if ($ExtraArguments.Count) { $command += '--'; $command += $ExtraArguments }
    Invoke-Dotnet $command
}
function Add-NotRun([string]$Name, [string]$Reason) {
    $steps.Add([pscustomobject]@{ name = $Name; status = 'not_run'; reason = $Reason })
}

Push-Location $PSScriptRoot
try {
    Invoke-Check 'repository-ignore-policy' { & ./tests/RepositoryPolicyChecks.ps1 }
    Invoke-Check 'build-assets' { & ./tests/BuildAssetChecks.ps1 }
    Invoke-Check 'source-security-policy' { & ./Verify-SecurityPolicy.ps1 }
    Invoke-Check 'installer-policy' { & ./tests/InstallerPolicyChecks.ps1 }
    if (-not $SkipRestore) {
        Invoke-Check 'locked-audited-restore' {
            $arguments = @('restore', './Localay.csproj', '-r', 'win-x64', '--locked-mode')
            if (-not $IsWindows) { $arguments += '-p:EnableWindowsTargeting=true' }
            Invoke-Dotnet $arguments
        }
    } else { Add-NotRun 'locked-audited-restore' 'SkipRestore requested; using previously restored assets. This is not a fresh dependency audit.' }
    Invoke-Check 'windows-release-build' {
        $arguments = @('build', './Localay.csproj', '-c', 'Release', '-r', 'win-x64', '--no-restore')
        if (-not $IsWindows) { $arguments += '-p:EnableWindowsTargeting=true' }
        Invoke-Dotnet $arguments
        $script:buildSucceeded = $true
    }
    Invoke-Check 'security-regressions' { Invoke-ProjectCheck 'SecurityChecks' }
    Invoke-Check 'startup-isolation' { Invoke-ProjectCheck 'StartupChecks' }
    Invoke-Check 'ui-preferences' { Invoke-ProjectCheck 'PreferenceChecks' }
    Invoke-Check 'preference-persistence' { Invoke-ProjectCheck 'PreferencePersistenceChecks' }
    Invoke-Check 'ocr-scene-regressions' { Invoke-ProjectCheck 'OcrSceneChecks' }
    Invoke-Check 'pipeline-timing-regressions' { Invoke-ProjectCheck 'PipelineTimingChecks' }
    Invoke-Check 'request-lifetime-regressions' { Invoke-ProjectCheck 'LifetimeChecks' }
    Invoke-Check 'translation-resilience-regressions' { Invoke-ProjectCheck 'TranslationResilienceChecks' }
    Invoke-Check 'overlay-layout-regressions' { Invoke-ProjectCheck 'OverlayLayoutChecks' }
    $ocrScript = Join-Path $ReportDirectory 'ocr-setup-command.ps1'
    Invoke-Check 'ocr-setup-regressions' { Invoke-ProjectCheck 'OcrSetupChecks' @('--script-path', $ocrScript) }
    Invoke-Check 'ocr-setup-script-regressions' { & ./tests/OcrSetupScriptChecks.ps1 -ScriptPath $ocrScript }
    Invoke-Check 'localization' { Invoke-ProjectCheck 'LocalizationChecks' }
    if ($buildSucceeded) {
        Invoke-Check 'identity-and-bundled-licenses' { Invoke-ProjectCheck 'BrandChecks' @('.', './bin/Release/Localay.dll') }
    } else { Add-NotRun 'identity-and-bundled-licenses' 'Release build did not succeed; no current assembly to inspect.' }
    if ($IsWindows -and $buildSucceeded) {
        Invoke-Check 'windows-wpf-integration' { Invoke-ProjectCheck 'UiWindowsChecks' }
    } elseif (-not $IsWindows) {
        Add-NotRun 'windows-wpf-integration' 'Windows WPF runtime is unavailable on this host. Run on Windows or use GitHub Actions.'
    } else { Add-NotRun 'windows-wpf-integration' 'Release build did not succeed.' }
} finally {
    Pop-Location
    $report = [ordered]@{
        schemaVersion = 1
        utc = [DateTime]::UtcNow.ToString('o')
        host = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        scope = 'Source policies, Release build, portable regressions, identity, localization and Windows WPF checks when supported.'
        limitations = @('Does not run installer lifecycle or Microsoft Defender; those are separate release workflow steps.', 'Does not verify physical screen capture, real OS preference changes, interactive use or physical DPI behavior.')
        passed = @($steps | Where-Object status -eq 'passed').Count
        failed = @($steps | Where-Object status -eq 'failed').Count
        notRun = @($steps | Where-Object status -eq 'not_run').Count
        steps = @($steps.ToArray())
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'summary.json') -Encoding utf8
}
if ($env:GITHUB_STEP_SUMMARY) {
    $table = @('## Localay validation', '', '| Check | Result |', '| --- | --- |')
    $table += $steps | ForEach-Object { "| $($_.name) | $($_.status) |" }
    $table | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
if ($report.failed -gt 0) {
    $failedNames = ($steps | Where-Object status -eq 'failed' | ForEach-Object name) -join ', '
    throw "$($report.failed) validation step(s) failed: $failedNames. See $ReportDirectory/summary.json."
}
Write-Host "Validation complete: $($report.passed) passed; $($report.notRun) not run. Report: $ReportDirectory/summary.json"
