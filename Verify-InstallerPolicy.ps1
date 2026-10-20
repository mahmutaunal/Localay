param([string]$InstallerScript = (Join-Path $PSScriptRoot "installer/Localay.iss"))
$ErrorActionPreference = "Stop"
$script = Get-Content -LiteralPath $InstallerScript -Raw
$setup = @{}
$section = ''
foreach ($line in $script -split '\r?\n') {
    if ($line -match '^\s*\[([^]]+)\]') { $section = $Matches[1]; continue }
    if ($section -eq 'Setup' -and $line -match '^\s*([^;=\s]+)\s*=(.*)$') {
        $key = $Matches[1]; $value = $Matches[2].Trim()
        if ($setup.ContainsKey($key)) { throw "Duplicate installer directive: $key" }
        $setup[$key] = $value
    }
}
$required = @{
    AppId = '{{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}'
    PrivilegesRequired = 'lowest'; PrivilegesRequiredOverridesAllowed = ''
    DefaultDirName = '{localappdata}\Programs\Localay'
    ArchitecturesAllowed = 'x64compatible'; ArchitecturesInstallIn64BitMode = 'x64compatible'
    AppMutex = 'Local\AlpWareStudio.Localay.Running'; SetupMutex = 'Local\AlpWareStudio.Localay.Setup'
    CloseApplications = 'no'; RestartApplications = 'no'; AlwaysRestart = 'no'
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
    $script -match '(?i)\b(download|external|shellexec|runascurrentuser|runasoriginaluser)\b\s*;' -or
    $script -match '(?im)^\s*Source:[^\r\n]*\b(?:external|download)\b' -or
    $script -match '(?i)DownloadTemporaryFile|AddExclusion|powershell(?:\.exe)?|cmd\.exe|taskkill|schtasks|netsh|Add-MpPreference|Set-MpPreference') {
    throw "Installer contains an unapproved helper, registry or deletion mechanism."
}
$run = [regex]::Match($script, '(?ims)^\[Run\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
$entries = @($run -split '\r?\n' | Where-Object { $_.Trim() -and $_ -notmatch '^\s*;' })
if ($entries.Count -ne 1 -or $entries[0] -notmatch '^Filename: "\{app\}\\Localay\.exe";' -or
    $entries[0] -notmatch 'postinstall' -or $entries[0] -notmatch 'skipifsilent') {
    throw "Only the user-selected, installed Localay executable may run after setup."
}
$appSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/App.xaml.cs') -Raw
foreach ($name in @($required.AppMutex)) {
    $literal = '"' + $name.Replace('\', '\\') + '"'
    if (-not $appSource.Contains($literal)) { throw "Installer/application lifecycle mutex names do not match: $name" }
}
Write-Host "Verified per-user installer identity, privilege policy, offline payload and user-data preservation."
