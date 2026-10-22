param([Parameter(Mandatory = $true)][string]$InstallerPath)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw "Installer is missing: $InstallerPath" }
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InstallerPath).Path)
if ($bytes.Length -lt 1024 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) { throw "Installer is not a Windows executable." }
$pe = [BitConverter]::ToInt32($bytes, 0x3c)
if ($pe -lt 0 -or $pe + 24 -ge $bytes.Length -or [BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) {
    throw "Installer PE header is invalid."
}
# Inno's loader always starts asInvoker and performs elevation itself based on
# PrivilegesRequired. This checks PE/loader compatibility, NOT installation scope.
# Verify-InstallerPolicy.ps1 and Windows install tests check that separate policy.
# See https://jrsoftware.org/is6help/topic_securitymeasures.htm
$text = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
if ($text -notmatch '<requestedExecutionLevel\b[^>]*\blevel\s*=\s*["'']asInvoker["'']') {
    throw "Expected the standard Inno asInvoker loader manifest."
}
if ($text -match '<requestedExecutionLevel\b[^>]*\blevel\s*=\s*["''](?:requireAdministrator|highestAvailable)["'']') {
    throw "Unexpected loader manifest: Inno handles elevation after its loader starts."
}
Write-Host "Verified Windows executable and Inno loader manifest; elevation policy is checked separately."
