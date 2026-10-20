$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$verify = Join-Path $root "Verify-InstallerPolicy.ps1"
& $verify
$original = Get-Content -LiteralPath (Join-Path $root "installer/Localay.iss") -Raw
$temporary = Join-Path ([IO.Path]::GetTempPath()) "localay-installer-policy-$([guid]::NewGuid()).iss"
try {
    $mutations = @(
        $original.Replace('PrivilegesRequired=lowest', 'PrivilegesRequired=admin'),
        $original.Replace('PrivilegesRequiredOverridesAllowed=', 'PrivilegesRequiredOverridesAllowed=dialog'),
        $original.Replace('DefaultDirName={localappdata}\Programs\Localay', 'DefaultDirName={autopf}\Localay'),
        ($original + "`n[UninstallDelete]`nType: filesandordirs; Name: `"{localappdata}\AlpWare Studio\Localay`"`n"),
        ($original + "`n[Registry]`nRoot: HKLM; Subkey: Software\Localay`n"),
        $original.Replace('skipifsilent', 'runhidden')
    )
    foreach ($mutation in $mutations) {
        Set-Content -LiteralPath $temporary -Value $mutation
        $rejected = $false
        try { & $verify -InstallerScript $temporary } catch { $rejected = $true }
        if (-not $rejected) { throw "An unsafe installer mutation passed validation." }
    }
} finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
Write-Host "Six unsafe installer mutations rejected."
