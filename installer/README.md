# Localay Windows installer

The main Windows download is `Localay-<version>-Setup-x64.exe`. The version comes from `Localay.csproj`. The GitHub Actions workflow produces the installer; the local `Publish-Windows.ps1` script can also produce a portable ZIP.

## User experience

- English/Turkish setup with the Localay logo and a short modern wizard.
- Installs for the current user in `%LOCALAPPDATA%\Programs\Localay`, without requesting administrator privileges. No all-users switch is offered.
- Includes the .NET runtime and all application files. No executable, prerequisite or model is downloaded during installation. Windows OCR's source-language feature must already be available or installed through Windows Settings.
- Creates a Start menu shortcut and offers an optional desktop shortcut. At the end the user can open Localay; silent setup never launches it.
- New installers reuse the same installation identity and folder. An installed higher application file version blocks downgrading. Close Localay before installing or uninstalling; Setup does not kill it or reboot Windows.
- Windows Settings can uninstall Localay. Settings, protected API credentials, profiles and logs remain in `%LOCALAPPDATA%\AlpWare Studio\Localay` across reinstall and uninstall. The installer does not delete that directory.

## Build in GitHub Actions from a Mac

Commit and push the project files, including `installer/`, `assets/`, build/verification scripts, `global.json`, `packages.lock.json` and regression checks. Open **Actions → Build Windows Installer → Run workflow**. The workflow runs only when manually dispatched; pushes and pull requests do not consume build minutes. No signing key or other new repository secret is required.

A successful run provides:

- **Localay-Setup-windows-x64**: setup EXE, its `.sha256` checksum and compiler/build metadata.

GitHub wraps downloaded artifacts in an artifact ZIP. Extract that wrapper once to obtain the single setup EXE distributed to users. The application itself no longer needs to be distributed as a folder of loose runtime files to installer users.

The workflow validates build assets, installer policy and compiler regression cases, installs and verifies the pinned compiler, restores locked/audited dependencies, publishes a self-contained Windows x64 application and builds setup. `Build-Installer.ps1` checks the payload and installer manifest before writing the checksum and build metadata. This workflow does not run Defender scans, application UI checks or install/reinstall/uninstall tests. Those scripts remain available for separate Windows validation.

## Compiler supply chain

`compiler.json` pins Inno Setup 6.7.3 to the official GitHub release asset and its release-provided SHA-256. `Install-Compiler.ps1` checks that hash and a valid Authenticode signature with publisher `Pyrsys B.V.` before running the compiler's official setup in current-user mode. `Verify-Compiler.ps1` compiles a minimal script with output disabled and checks the loaded engine's exact version. It deliberately does not use `ISCC.exe`'s placeholder PE file version. Both compiler installation and installer building use this check. It is a CI build-tool downloader, never an end-user bootstrapper or a shipped application component. Update the version/hash/publisher together after reviewing a new official release. Follow the upstream compiler license for your use.

For a local Windows PowerShell 7 build:

```powershell
./installer/Install-Compiler.ps1
# The script sets LOCALAY_ISCC for this PowerShell session.
./Publish-Windows.ps1
```

For building only setup from an already verified publish folder:

```powershell
./Build-Installer.ps1 -PublishDirectory ./artifacts/publish
```

Compilation alone is not a malware scan. The optional local `Publish-Windows.ps1` path scans the unpacked application, portable package and installer with Defender and fails if scanning cannot complete. These scans and `tests/InstallerSmoke.ps1` are separate from the manual installer workflow. Do not describe the workflow artifact as scanned or installation-tested unless those checks have actually run.

## Unsigned delivery

The installer, application and generated uninstaller remain unsigned. SmartScreen, Smart App Control, enterprise policies or other antivirus engines can warn or block despite clean scans. CI cannot establish real download reputation or guarantee every endpoint's behavior. A release should accurately describe its available validation rather than promise zero warnings. If a detection occurs, investigate the named file and submit the exact sample to the vendor through its developer false-positive process; do not ask users to disable protection.

References: [Inno per-user privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm), [official compiler download](https://jrsoftware.org/isdl.php), [pinned compiler release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3), [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

For a separate Defender validation on GitHub-hosted Windows runners, `.github/scripts/Prepare-Defender.ps1` handles the [official runner image's Defender configuration](https://github.com/actions/runner-images/blob/main/images/windows/scripts/build/Configure-WindowsDefender.ps1). The scan command and scan-only behavior follow [Microsoft's MpCmdRun documentation](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus).
