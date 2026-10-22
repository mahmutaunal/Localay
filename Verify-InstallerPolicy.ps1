param([string]$InstallerScript = (Join-Path $PSScriptRoot "installer/Localay.iss"))
$ErrorActionPreference = "Stop"
$script = Get-Content -LiteralPath $InstallerScript -Raw
$setup = @{}
$section = ''
$seenSections = @{}
foreach ($line in $script -split '\r?\n') {
    if ($line -match '^\s*\[([^]]+)\]') {
        $section = $Matches[1]
        if ($seenSections.ContainsKey($section)) { throw "Duplicate installer section: $section" }
        $seenSections[$section] = $true
        continue
    }
    if ($section -eq 'Setup' -and $line -match '^\s*([^;=\s]+)\s*=(.*)$') {
        $key = $Matches[1]; $value = $Matches[2].Trim()
        if ($setup.ContainsKey($key)) { throw "Duplicate installer directive: $key" }
        $setup[$key] = $value
    }
}
$required = @{
    AppId = '{{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}'
    PrivilegesRequired = 'admin'; PrivilegesRequiredOverridesAllowed = ''
    DefaultDirName = '{autopf}\Localay'
    ArchitecturesAllowed = 'x64compatible'; ArchitecturesInstallIn64BitMode = 'x64compatible'
    AppMutex = 'Local\AlpWareStudio.Localay.Running'; SetupMutex = 'Local\AlpWareStudio.Localay.Setup'
    CloseApplications = 'no'; RestartApplications = 'no'; AlwaysRestart = 'no'
    RestartIfNeededByRun = 'no'; DisableProgramGroupPage = 'yes'
    Uninstallable = 'yes'; CreateUninstallRegKey = 'yes'; UsePreviousAppDir = 'yes'
    SetupIconFile = '{#RepoRoot}\assets\localay.ico'
}
foreach ($entry in $required.GetEnumerator()) {
    if (-not $setup.ContainsKey($entry.Key) -or $setup[$entry.Key] -cne $entry.Value) {
        throw "Installer security policy violation: $($entry.Key)"
    }
}
# No custom registry, uninstall helpers, recursive deletion or on-device downloads.
if ($script -match '(?im)^\s*\[(Registry|InstallDelete|UninstallDelete|UninstallRun)\]' -or
    $script -match '(?i)\b(download|external|shellexec|runascurrentuser)\b\s*;' -or
    $script -match '(?im)^\s*Source:[^\r\n]*\b(?:external|download)\b' -or
    $script -match '(?i)DownloadTemporaryFile|AddExclusion|powershell(?:\.exe)?|cmd\.exe|taskkill|schtasks|netsh|Add-MpPreference|Set-MpPreference') {
    throw "Installer contains an unapproved helper, registry or deletion mechanism."
}
$run = [regex]::Match($script, '(?ims)^\[Run\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
$entries = @($run -split '\r?\n' | Where-Object { $_.Trim() -and $_ -notmatch '^\s*;' })
if ($entries.Count -ne 1 -or $entries[0] -notmatch '^Filename: "\{app\}\\Localay\.exe";' -or
    $entries[0] -notmatch '; Flags: nowait postinstall skipifsilent runasoriginaluser\s*$') {
    throw "Only the user-selected, installed Localay executable may run after setup."
}
# Start menu remains automatic; desktop is the only optional shortcut task.
$tasks = [regex]::Match($script, '(?ims)^\[Tasks\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
$taskEntries = @($tasks -split '\r?\n' | Where-Object { $_.Trim() -and $_ -notmatch '^\s*;' })
if ($taskEntries.Count -ne 1 -or
    $taskEntries[0] -cne 'Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked') {
    throw "Only the existing optional desktop shortcut task is allowed."
}
$icons = [regex]::Match($script, '(?ims)^\[Icons\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
$iconEntries = @($icons -split '\r?\n' | Where-Object { $_.Trim() -and $_ -notmatch '^\s*;' })
if ($iconEntries.Count -ne 2 -or
    $iconEntries[0] -cne 'Name: "{autoprograms}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"' -or
    $iconEntries[1] -cne 'Name: "{autodesktop}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"; Tasks: desktopicon') {
    throw "Start menu must be automatic and desktop optional, both in the installation scope."
}
if ($script -notmatch "RegQueryStringValue\(HKLM64, UninstallKey," -or
    $script -notmatch "if HasLegacyUserInstall then" -or
    $script -notmatch "Result := CustomMessage\('LegacyUserInstall'\)") {
    throw "Installer must check machine-wide versions and block legacy per-user conflicts."
}
$appSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/App.xaml.cs') -Raw
foreach ($name in @($required.AppMutex)) {
    $literal = '"' + $name.Replace('\', '\\') + '"'
    if (-not $appSource.Contains($literal)) { throw "Installer/application lifecycle mutex names do not match: $name" }
}
Write-Host "Verified administrator-required installer, automatic Start menu, original-user launch and user-data preservation."
