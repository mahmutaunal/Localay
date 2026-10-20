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
$working = Join-Path $env:RUNNER_TEMP "Localay-InstallCheck-$([guid]::NewGuid())"
$installDir = Join-Path $working 'application'
$logs = Join-Path $root 'artifacts\installer-tests'
New-Item -ItemType Directory -Force -Path $working, $logs | Out-Null
$dataDir = Join-Path $env:LOCALAPPDATA 'AlpWare Studio\Localay'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$sentinel = Join-Path $dataDir "installer-preservation-$([guid]::NewGuid()).txt"
$sentinelText = [guid]::NewGuid().ToString()
Set-Content -LiteralPath $sentinel -Value $sentinelText -NoNewline
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Localay.lnk'
if (Test-Path -LiteralPath $shortcut) { throw "Refusing to replace an existing shortcut." }

function Invoke-TestProcess([string]$Executable, [string[]]$ProcessArguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $ProcessArguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        $process.Kill($true)
        throw "Installer process exceeded three minutes: $Executable"
    }
    $process.Refresh()
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
    if (-not (Test-Path $userKey)) { throw "Per-user uninstall registration is missing." }
    $registration = Get-ItemProperty -LiteralPath $userKey
    if ($registration.DisplayName -ne 'Localay' -or
        $registration.InstallLocation.TrimEnd('\') -ine $installDir.TrimEnd('\')) {
        throw "Per-user registration refers to the wrong installation."
    }
    if ($machineKeys | Where-Object { Test-Path $_ }) { throw "Setup created a machine-wide registration." }
    if (-not (Test-Path -LiteralPath $shortcut)) { throw "Start menu shortcut is missing." }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    if ($link.TargetPath -ine (Join-Path $installDir 'Localay.exe')) { throw "Start shortcut targets the wrong program." }
    Assert-PreservedData
}
try {
    Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
        "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $logs 'install.log')`"")
    Assert-InstalledPayload
    if (Get-Process -Name Localay -ErrorAction SilentlyContinue) { throw "Silent setup unexpectedly launched Localay." }
    # Replaying the same installer exercises replacement and uninstall-log reuse.
    Invoke-TestProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
        "/LOG=`"$(Join-Path $logs 'reinstall.log')`"")
    Assert-InstalledPayload
    $uninstaller = Join-Path $installDir 'unins000.exe'
    & (Join-Path $root 'Verify-Installer.ps1') -InstallerPath $uninstaller
    # Scan the extracted files and generated uninstaller, not only the compressed installer.
    & (Join-Path $root 'Scan-Windows.ps1') -PathToScan $installDir -ReportName 'defender-installed.json'
    Invoke-TestProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/LOG=`"$(Join-Path $logs 'uninstall.log')`"")
    if ((Test-Path $userKey) -or (Test-Path -LiteralPath (Join-Path $installDir 'Localay.exe')) -or
        (Test-Path -LiteralPath $shortcut)) { throw "Uninstallation left the application registration, executable or shortcut behind." }
    Assert-PreservedData
    [pscustomobject]@{
        Install = 'passed'; Reinstall = 'passed'; Uninstall = 'passed'; PayloadHashes = 'matched'
        UserData = 'preserved'; Registration = 'HKCU only'; SilentLaunch = 'not launched'
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        Limitations = 'Automated runner checks; no interactive UI, download reputation or game capture test.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'results.json')
} finally {
    # Remove only this test's sentinel; never recursively delete the user-data directory.
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}
Write-Host "Installer install/reinstall/uninstall, payload hashes and user-data preservation checks passed."
