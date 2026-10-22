param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)
$ErrorActionPreference = "Stop"
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') {
    throw "Run these install/uninstall checks only on a disposable GitHub Actions Windows runner."
}
$root = Split-Path -Parent $PSScriptRoot
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
& (Join-Path $root 'Verify-Installer.ps1') -InstallerPath $installer
$keyName = '{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}_is1'
$userKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$keyName"
$machineKeys = @("HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$keyName",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$keyName")
if ((Test-Path $userKey) -or ($machineKeys | Where-Object { Test-Path $_ })) {
    throw "Refusing to change an existing Localay installation."
}
$installDir = Join-Path $env:ProgramW6432 'Localay'
if (Test-Path -LiteralPath $installDir) { throw 'Refusing to change an existing installation directory.' }
$logs = Join-Path $root 'artifacts\installer-tests'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$dataDir = Join-Path $env:LOCALAPPDATA 'AlpWare Studio\Localay'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$sentinel = Join-Path $dataDir "installer-preservation-$([guid]::NewGuid()).txt"
$sentinelText = [guid]::NewGuid().ToString()
Set-Content -LiteralPath $sentinel -Value $sentinelText -NoNewline
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Localay.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Localay.lnk'
if ((Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $desktopShortcut)) { throw "Refusing to replace an existing shortcut." }

function Invoke-TestProcess([string]$Executable, [string[]]$ProcessArguments, [switch]$ExpectFailure) {
    $process = Start-Process -FilePath $Executable -ArgumentList $ProcessArguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        $process.Kill($true)
        throw "Installer process exceeded three minutes: $Executable"
    }
    $process.Refresh()
    if ($ExpectFailure) {
        if ($process.ExitCode -eq 0) { throw "Setup accepted an installation that should be blocked." }
        return
    }
    if ($process.ExitCode -ne 0) { throw "Installer process failed with exit $($process.ExitCode): $Executable" }
}
function Assert-PreservedData {
    if (-not (Test-Path -LiteralPath $sentinel) -or (Get-Content -LiteralPath $sentinel -Raw) -cne $sentinelText) {
        throw "Installer or uninstaller changed existing user data."
    }
}
function Assert-InstalledPayload {
    & (Join-Path $root 'Verify-Publish.ps1') -PublishDirectory $installDir
    foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName)
        $target = Join-Path $installDir $relative
        if (-not (Test-Path -LiteralPath $target) -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw "Installed payload mismatch: $relative"
        }
    }
    if (-not (Test-Path $machineKeys[0])) { throw "64-bit machine-wide uninstall registration is missing." }
    $registration = Get-ItemProperty -LiteralPath $machineKeys[0]
    if ($registration.DisplayName -ne 'Localay' -or
        $registration.InstallLocation.TrimEnd('\') -ine $installDir.TrimEnd('\')) {
        throw "Machine-wide registration refers to the wrong installation."
    }
    if ((Test-Path $userKey) -or (Test-Path $machineKeys[1])) { throw "Unexpected user or 32-bit registration." }
    if (-not (Test-Path -LiteralPath $shortcut)) { throw "Start menu shortcut is missing." }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    if ($link.TargetPath -ine (Join-Path $installDir 'Localay.exe')) { throw "Start shortcut targets the wrong program." }
    Assert-PreservedData
}
try {
    # A loaded per-user registration must block the scope transition without
    # executing its UninstallString or silently creating a second installation.
    try {
        New-Item -Path $userKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $userKey -Name DisplayName -Value 'Localay'
        Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=english',
            "/LOG=`"$(Join-Path $logs 'legacy-block.log')`"") -ExpectFailure
        if ((Get-Content -LiteralPath (Join-Path $logs 'legacy-block.log') -Raw) -notmatch 'previous per-user Localay installation') {
            throw 'Setup did not report the expected legacy conflict.'
        }
        if ((Test-Path $machineKeys[0]) -or (Test-Path -LiteralPath $installDir)) {
            throw 'Legacy conflict check wrote a machine-wide installation.'
        }
        if (-not (Test-Path $userKey)) { throw 'Legacy conflict check removed the old registration.' }
        Assert-PreservedData
    } finally { Remove-Item -LiteralPath $userKey -Force -ErrorAction SilentlyContinue }
    Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=english',
        "/LOG=`"$(Join-Path $logs 'install.log')`"")
    Assert-InstalledPayload
    if (Test-Path -LiteralPath $desktopShortcut) { throw "Unchecked desktop task created a shortcut." }
    if (Get-Process -Name Localay -ErrorAction SilentlyContinue) { throw "Silent setup unexpectedly launched Localay." }
    # Replaying setup reuses the machine installation; select the existing desktop task.
    Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=english',
        '/TASKS="desktopicon"', "/LOG=`"$(Join-Path $logs 'reinstall.log')`"")
    Assert-InstalledPayload
    if (-not (Test-Path -LiteralPath $desktopShortcut)) { throw 'Selected desktop task did not create a shortcut.' }
    # Higher machine version must refuse a downgrade and leave the payload intact.
    $installedVersion = (Get-ItemProperty -LiteralPath $machineKeys[0]).DisplayVersion
    try {
        Set-ItemProperty -LiteralPath $machineKeys[0] -Name DisplayVersion -Value '9999.0.0'
        Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=english',
            "/LOG=`"$(Join-Path $logs 'downgrade-block.log')`"") -ExpectFailure
        if ((Get-Content -LiteralPath (Join-Path $logs 'downgrade-block.log') -Raw) -notmatch 'newer version of Localay') {
            throw 'Setup did not report the expected downgrade conflict.'
        }
        if ((Get-ItemProperty -LiteralPath $machineKeys[0]).DisplayVersion -ne '9999.0.0') {
            throw 'Blocked downgrade modified the installed version registration.'
        }
        Assert-InstalledPayload
    } finally { Set-ItemProperty -LiteralPath $machineKeys[0] -Name DisplayVersion -Value $installedVersion }
    if (Get-Process -Name Localay -ErrorAction SilentlyContinue) { throw 'Silent reinstall unexpectedly launched Localay.' }
    $uninstaller = Join-Path $installDir 'unins000.exe'
    & (Join-Path $root 'Verify-Installer.ps1') -InstallerPath $uninstaller
    # Scan the extracted files and generated uninstaller, not only the compressed installer.
    & (Join-Path $root 'Scan-Windows.ps1') -PathToScan $installDir -ReportName 'defender-installed.json'
    Invoke-TestProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/LOG=`"$(Join-Path $logs 'uninstall.log')`"")
    if ((Test-Path $userKey) -or ($machineKeys | Where-Object { Test-Path $_ }) -or
        (Test-Path -LiteralPath (Join-Path $installDir 'Localay.exe')) -or
        (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $desktopShortcut)) { throw "Uninstallation left the application registration, executable or shortcut behind." }
    Assert-PreservedData
    [pscustomobject]@{
        Install = 'passed'; Reinstall = 'passed'; Uninstall = 'passed'; PayloadHashes = 'matched'
        UserData = 'preserved'; Registration = 'HKLM 64-bit only'; SilentLaunch = 'not launched'
        LegacyConflict = 'blocked'; Downgrade = 'blocked'; DesktopTask = 'optional'; StartMenu = 'automatic'
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        Limitations = 'Automated runner checks; no interactive UAC/original-user launch, download reputation or game capture test.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'results.json')
} finally {
    # Remove only this test's sentinel; never recursively delete the user-data directory.
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}
Write-Host "Installer install/reinstall/uninstall, payload hashes and user-data preservation checks passed."
