# Localay Windows installer

The main Windows download is `Localay-<version>-Setup-x64.exe`. The version comes from `Localay.csproj`; the existing portable ZIP remains a separate optional artifact.

## User experience

- English/Turkish setup with the Localay logo and a short modern wizard.
- Installs for the current user in `%LOCALAPPDATA%\Programs\Localay`, without requesting administrator privileges. No all-users switch is offered.
- Includes the .NET runtime and all application files. No executable, prerequisite or model is downloaded during installation. Windows OCR's source-language feature must already be available or installed through Windows Settings.
- Creates a Start menu shortcut and offers an optional desktop shortcut. At the end the user can open Localay; silent setup never launches it.
- New installers reuse the same installation identity and folder. An installed higher application file version blocks downgrading. Close Localay before installing or uninstalling; Setup does not kill it or reboot Windows.
- Windows Settings can uninstall Localay. Settings, protected API credentials, profiles and logs remain in `%LOCALAPPDATA%\AlpWare Studio\Localay` across reinstall and uninstall. The installer does not delete that directory.

## Build in GitHub Actions from a Mac

Commit and push the project files, including `installer/`, `assets/`, build/verification scripts, `global.json`, `packages.lock.json` and regression checks. Open **Actions → Build Windows Installer → Run workflow**. Pushes to `main` and `codex/**` and pull requests also trigger it. No signing key or other new repository secret is required.

A successful run provides:

- **Localay-Setup-windows-x64**: setup EXE, its `.sha256` checksum and compiler/build metadata.
- **Localay-Portable-windows-x64**: optional portable ZIP/checksum.
- **Localay-validation**: Defender reports with file hashes, dependency audit and automatic install/reinstall/uninstall results and logs. Reports available before a failure are also retained; installers are uploaded only when preceding required checks succeed.

GitHub wraps downloaded artifacts in an artifact ZIP. Extract that wrapper once to obtain the single setup EXE distributed to users. The application itself no longer needs to be distributed as a folder of loose runtime files to installer users.

The workflow verifies source/payload policy, restores locked and audited dependencies, runs Windows security checks, installs the pinned compiler, publishes a clean application, builds both formats and scans them. A disposable Windows runner then silently installs, compares every published file's hash, checks HKCU-only registration and the shortcut, reinstalls, scans extracted files including the uninstaller, uninstalls, and verifies existing user data survived. This tests the same-version replacement flow; it is not an interactive UI, game capture or genuine browser-download reputation test.

## Compiler supply chain

`compiler.json` pins Inno Setup 6.7.3 to the official GitHub release asset and its release-provided SHA-256. `Install-Compiler.ps1` checks that hash and a valid Authenticode signature with publisher `Pyrsys B.V.` before running the compiler's official setup in current-user mode. It is a CI build-tool downloader, never an end-user bootstrapper or a shipped application component. Update the version/hash/publisher together after reviewing a new official release. Follow the upstream compiler license for your use.

For a local Windows PowerShell 7 build:

```powershell
./installer/Install-Compiler.ps1
# Outside GitHub Actions, set LOCALAY_ISCC to the verified ISCC.exe path printed above.
$env:LOCALAY_ISCC = '<verified compiler directory>\ISCC.exe'
./Publish-Windows.ps1
```

For building only setup from an already verified publish folder:

```powershell
./Build-Installer.ps1 -PublishDirectory ./artifacts/publish
```

Compilation alone is not a malware scan. The full publish script scans the unpacked application, portable package and installer. CI additionally scans the installed uninstaller. Missing/disabled Defender, signature update failures, scanner errors, detections or test failures block the normal artifact upload. The stock GitHub Windows image disables several Defender scans and excludes entire disks. Before release scanning, `.github/scripts/Prepare-Defender.ps1` removes those inherited exclusions and enables scanning on the disposable GitHub-hosted runner only. It refuses to run on end-user PCs or self-hosted machines. Archive scanning must be enabled; an empty folder or file/hash changes during scanning fail validation. Scan-only mode reports detections without silently remediating a release into a different artifact. The workflow never disables protection or adds exclusions.

## Unsigned delivery

The installer, application and generated uninstaller remain unsigned. SmartScreen, Smart App Control, enterprise policies or other antivirus engines can warn or block despite clean scans. CI cannot establish real download reputation or guarantee every endpoint's behavior. A release should accurately describe its available validation rather than promise zero warnings. If a detection occurs, investigate the named file and submit the exact sample to the vendor through its developer false-positive process; do not ask users to disable protection.

References: [Inno per-user privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm), [official compiler download](https://jrsoftware.org/isdl.php), [pinned compiler release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3), [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

The runner preparation is necessary because of the [official runner image's Defender configuration](https://github.com/actions/runner-images/blob/main/images/windows/scripts/build/Configure-WindowsDefender.ps1). The scan command and scan-only behavior follow [Microsoft's MpCmdRun documentation](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus).
