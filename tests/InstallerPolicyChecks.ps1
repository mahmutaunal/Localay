$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$verify = Join-Path $root "Verify-InstallerPolicy.ps1"
& $verify
$original = Get-Content -LiteralPath (Join-Path $root "installer/Localay.iss") -Raw
$temporary = Join-Path ([IO.Path]::GetTempPath()) "localay-installer-policy-$([guid]::NewGuid()).iss"
try {
    $mutations = @(
        $original.Replace('PrivilegesRequired=admin', 'PrivilegesRequired=lowest'),
        $original.Replace('PrivilegesRequiredOverridesAllowed=', 'PrivilegesRequiredOverridesAllowed=dialog'),
        $original.Replace('DefaultDirName={autopf}\Localay', 'DefaultDirName={localappdata}\Programs\Localay'),
        ($original + "`n[UninstallDelete]`nType: filesandordirs; Name: `"{localappdata}\AlpWare Studio\Localay`"`n"),
        ($original + "`n[Registry]`nRoot: HKLM; Subkey: Software\Localay`n"),
        $original.Replace('skipifsilent', 'runhidden'),
        $original.Replace('runasoriginaluser', 'runascurrentuser'),
        $original.Replace(' runasoriginaluser', ''),
        $original.Replace('{autoprograms}', '{userprograms}'),
        $original.Replace('Name: "{autoprograms}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"', 'Name: "{autoprograms}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"; Tasks: desktopicon'),
        $original.Replace('Flags: unchecked', 'Flags: checkedonce'),
        $original.Replace('RegQueryStringValue(HKLM64, UninstallKey,', 'RegQueryStringValue(HKCU, UninstallKey,'),
        $original.Replace('if HasLegacyUserInstall then', 'if False then'),
        ($original + "`n[Tasks]`nName: `"startmenu`"; Description: `"Start menu`"`n")
    )
    $count = 0
    foreach ($mutation in $mutations) {
        $count++
        if ($mutation -ceq $original) { throw "Mutation $count did not change the script." }
        Set-Content -LiteralPath $temporary -Value $mutation
        $rejected = $false
        try { & $verify -InstallerScript $temporary } catch { $rejected = $true }
        if (-not $rejected) { throw "Unsafe installer mutation $count passed validation." }
    }
} finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
Write-Host "$($mutations.Count) unsafe installer mutations rejected."
