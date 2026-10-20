param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Localay.csproj') -Raw
$installer = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'installer/Localay.iss') -Raw
$paths = @($project.Project.PropertyGroup.ApplicationIcon | Where-Object { $_ })
$paths += @($project.Project.ItemGroup.Resource.Include | Where-Object { $_ })
foreach ($directive in @('SetupIconFile', 'WizardSmallImageFile')) {
    $match = [regex]::Match($installer, "(?m)^$directive=(.+)\r?$")
    if (-not $match.Success -or -not $match.Groups[1].Value.StartsWith('{#RepoRoot}\')) {
        throw "Expected a repository asset for installer directive $directive."
    }
    $paths += $match.Groups[1].Value.Trim().Substring('{#RepoRoot}\'.Length)
}
foreach ($relative in @($paths | Sort-Object -Unique)) {
    $path = Join-Path $RepositoryRoot ($relative.Replace('\', '/'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required build asset is missing: $relative. Restore it and include it in the commit."
    }
    $bytes = [IO.File]::ReadAllBytes($path)
    switch ([IO.Path]::GetExtension($path).ToLowerInvariant()) {
        '.ico' {
            if ($bytes.Length -lt 22 -or [BitConverter]::ToUInt32($bytes, 0) -ne 65536) {
                throw "Invalid Windows ICO header: $relative"
            }
            $count = [BitConverter]::ToUInt16($bytes, 4)
            if ($count -eq 0 -or $bytes.Length -lt (6 + 16 * $count)) { throw "Invalid ICO directory: $relative" }
            for ($i = 0; $i -lt $count; $i++) {
                $entry = 6 + 16 * $i
                $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
                $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)
                if ($length -eq 0 -or $offset -lt (6 + 16 * $count) -or ([long]$offset + $length) -gt $bytes.Length) {
                    throw "Truncated ICO image: $relative"
                }
            }
        }
        '.png' {
            if ($bytes.Length -lt 33 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A' -or
                [Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') {
                throw "Invalid PNG header: $relative"
            }
        }
        default { throw "Unsupported build image format: $relative" }
    }
    Write-Host "Verified build asset: $relative"
}
