param([Parameter(Mandatory = $true)][string]$PublishDirectory)
$ErrorActionPreference = "Stop"
$verify = Join-Path (Split-Path -Parent $PSScriptRoot) "Verify-Publish.ps1"
& $verify -PublishDirectory $PublishDirectory
$sentinel = Join-Path $PublishDirectory "config.txt"
if (Test-Path -LiteralPath $sentinel) { throw "Refusing to replace an existing config file in regression checks." }
try {
    Set-Content -LiteralPath $sentinel -Value "security-test-sentinel"
    $rejected = $false
    try { & $verify -PublishDirectory $PublishDirectory } catch { $rejected = $true }
    if (-not $rejected) { throw "Publish validation accepted private settings in the release." }
} finally { Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue }
$runtimePath = Join-Path $PublishDirectory "Localay.runtimeconfig.json"
$original = [IO.File]::ReadAllBytes($runtimePath)
try {
    $runtime = [Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json
    $runtime.runtimeOptions.tfm = "net9.0"
    $runtime | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $runtimePath
    $rejected = $false
    try { & $verify -PublishDirectory $PublishDirectory } catch { $rejected = $true }
    if (-not $rejected) { throw "Publish validation accepted the retired runtime target." }
} finally { [IO.File]::WriteAllBytes($runtimePath, $original) }
foreach ($language in @('en', 'tr')) {
    $languagePath = Join-Path $PublishDirectory "Languages/$language.json"
    $originalLanguage = [IO.File]::ReadAllBytes($languagePath)
    try {
        Remove-Item -LiteralPath $languagePath
        $rejected = $false
        try { & $verify -PublishDirectory $PublishDirectory } catch { $rejected = $true }
        if (-not $rejected) { throw "Publish validation accepted missing $language resources." }
    } finally { [IO.File]::WriteAllBytes($languagePath, $originalLanguage) }
}
$languagePath = Join-Path $PublishDirectory 'Languages/tr.json'
$originalLanguage = [IO.File]::ReadAllBytes($languagePath)
try {
    $language = [Text.Encoding]::UTF8.GetString($originalLanguage) | ConvertFrom-Json -AsHashtable
    $language['Main_Start'] = 'Untranslated stale package text'
    $language | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $languagePath -Encoding utf8
    $rejected = $false
    try { & $verify -PublishDirectory $PublishDirectory } catch { $rejected = $true }
    if (-not $rejected) { throw 'Publish validation accepted stale Turkish resources.' }
} finally { [IO.File]::WriteAllBytes($languagePath, $originalLanguage) }
& $verify -PublishDirectory $PublishDirectory
Write-Host "Publish regression checks passed: private data, wrong runtime, missing language files and stale Turkish resources are rejected."
