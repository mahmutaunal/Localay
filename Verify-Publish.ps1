param([Parameter(Mandatory = $true)][string]$PublishDirectory, [switch]$IncludeOneOcrAssets)
$ErrorActionPreference = "Stop"
$required = @(
    "Localay.exe", "Localay.dll", "Localay.deps.json", "Localay.runtimeconfig.json",
    "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll",
    "PresentationFramework.dll", "wpfgfx_cor3.dll", "WinRT.Runtime.dll",
    "Microsoft.Windows.SDK.NET.dll",
    "Languages\en.json", "Languages\tr.json"
)
if ($IncludeOneOcrAssets) { $required += @("oneocr.dll", "oneocr.onemodel", "ONEOCR-LICENSE.txt") }
foreach ($file in $required) {
    $path = Join-Path $PublishDirectory $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required publish file is missing or empty: $file"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $PublishDirectory "Localay.runtimeconfig.json") -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or -not $runtime.runtimeOptions.includedFrameworks) {
    throw "The publish output is not self-contained."
}
$deps = Get-Content -LiteralPath (Join-Path $PublishDirectory "Localay.deps.json") -Raw | ConvertFrom-Json
if ($deps.runtimeTarget.name -notmatch '/win-x64$') { throw "Unexpected runtime target: $($deps.runtimeTarget.name)" }
$retired = '^(Whisper\.|NAudio(?:\.|/)|org\.k2fsa\.sherpa|System\.Speech/|Microsoft\.ML\.OnnxRuntime|Microsoft\.Windows\.CsWinRT/)'
$unexpected = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match $retired }
if ($unexpected) { throw "Retired dependencies in publish output: $($unexpected -join ', ')" }
$unexpectedFiles = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
    $_.Name -match '(?i)(whisper|sherpa|onnxruntime|NAudio|System\.Speech|cublas|cudart|vulkan)'
}
if ($unexpectedFiles) { throw "Retired speech binaries in publish output: $($unexpectedFiles.Name -join ', ')" }
# Validate the PE header rather than trusting the package directory's name.
$peFiles = @("Localay.exe", "coreclr.dll")
if ($IncludeOneOcrAssets) { $peFiles += "oneocr.dll" }
foreach ($file in $peFiles) {
    $stream = [IO.File]::OpenRead((Join-Path $PublishDirectory $file))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE binary: $file" }
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Not a Windows x64 PE binary: $file" }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
Get-ChildItem -LiteralPath (Join-Path $PublishDirectory "Languages") -Filter *.json | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable | Out-Null
}
# A syntactically valid but stale language file must not enter a new release.
foreach ($language in @('en', 'tr')) {
    $sourceLanguage = Join-Path $PSScriptRoot "app/Languages/$language.json"
    $publishedLanguage = Join-Path $PublishDirectory "Languages/$language.json"
    if ((Get-FileHash -LiteralPath $sourceLanguage -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $publishedLanguage -Algorithm SHA256).Hash) {
        throw "Published language resource differs from the checked source: $language"
    }
}
if ($runtime.runtimeOptions.tfm -ne "net10.0") { throw "Expected supported .NET 10 runtime." }
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    if ($framework.version -notlike "10.0.*") { throw "Unexpected framework version: $($framework.version)" }
}
$exeBytes = [IO.File]::ReadAllBytes((Join-Path $PublishDirectory "Localay.exe"))
$exeText = [Text.Encoding]::UTF8.GetString($exeBytes)
if ($exeText -notmatch '<requestedExecutionLevel\s+level="asInvoker"\s+uiAccess="false"') {
    throw "The executable must run as the current user, without UIAccess."
}
$privateFiles = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
    $_.Name -match '(?i)(^config(?:_backup_.*)?\.txt$|_config\.txt$|last_.*\.(txt|json)$|crash_log\.txt$|\.(bat|cmd|ps1)$)' -or
    $_.Directory.Name -in @("Profiles", "Logs", "Content")
}
if ($privateFiles) { throw "Private settings, diagnostics or scripts found in publish output: $($privateFiles.Name -join ', ')" }
if (-not $IncludeOneOcrAssets) {
    if ((Test-Path (Join-Path $PublishDirectory "oneocr.dll")) -or (Test-Path (Join-Path $PublishDirectory "oneocr.onemodel"))) {
        throw "Default distributions use Windows OCR and must not contain unapproved OneOCR assets."
    }
} else {
    $expectedHashes = Get-Content -LiteralPath (Join-Path $PSScriptRoot "security/native-assets.json") -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in $expectedHashes.GetEnumerator()) {
        $assetPath = Join-Path $PublishDirectory $entry.Key
        if ((Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash -ne $entry.Value) {
            throw "Unrecognized native OCR asset: $($entry.Key)"
        }
    }
    if (-not $IsWindows) { throw "OneOCR distributions require Windows Authenticode validation." }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $PublishDirectory "oneocr.dll")
    if ($signature.Status -ne "Valid" -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
        throw "OneOCR must have a valid Microsoft Authenticode signature."
    }
}
Write-Host "Verified .NET 10 win-x64 runtime, standard-user manifest, clean distribution and OCR policy."
