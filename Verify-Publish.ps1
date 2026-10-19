param([Parameter(Mandatory = $true)][string]$PublishDirectory)
$ErrorActionPreference = "Stop"
$required = @(
    "Localay.exe", "Localay.dll", "Localay.deps.json", "Localay.runtimeconfig.json",
    "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll",
    "PresentationFramework.dll", "wpfgfx_cor3.dll", "WinRT.Runtime.dll",
    "Microsoft.Windows.SDK.NET.dll", "oneocr.dll", "oneocr.onemodel",
    "Languages\en.json"
)
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
foreach ($file in @("Localay.exe", "coreclr.dll", "oneocr.dll")) {
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
Write-Host "Verified self-contained win-x64 runtime, WPF/WinRT, OCR assets, languages and dependency cleanup."
