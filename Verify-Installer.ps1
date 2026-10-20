param([Parameter(Mandatory = $true)][string]$InstallerPath)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw "Installer is missing: $InstallerPath" }
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InstallerPath).Path)
if ($bytes.Length -lt 1024 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) { throw "Installer is not a Windows executable." }
$pe = [BitConverter]::ToInt32($bytes, 0x3c)
if ($pe -lt 0 -or $pe + 24 -ge $bytes.Length -or [BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) {
    throw "Installer PE header is invalid."
}
# Inno's setup loader embeds its UAC manifest as UTF-8 or UTF-16 XML.
$text = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
if ($text -notmatch '<requestedExecutionLevel\b[^>]*\blevel\s*=\s*["'']asInvoker["'']') {
    throw "Installer must have a standard-user asInvoker manifest."
}
if ($text -match '<requestedExecutionLevel\b[^>]*\blevel\s*=\s*["''](?:requireAdministrator|highestAvailable)["'']') {
    throw "Installer must not request elevation."
}
Write-Host "Verified Windows installer and non-elevating manifest."
