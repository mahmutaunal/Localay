# Localay Windows installer

The main Windows download is `Localay-<version>-Setup-x64.exe`. The version comes from `Localay.csproj`. The GitHub Actions workflow produces the installer; the local `Publish-Windows.ps1` script can also produce a portable ZIP.

## User experience

- English/Turkish setup with the Localay logo and a short modern wizard.
- Requests administrator permission through Windows UAC automatically and installs for all users in `%ProgramFiles%\Localay`. No installation-scope switch is offered. UAC consent or administrator credentials are still required by Windows.
- Includes the .NET runtime and all application files. No executable, prerequisite or model is downloaded during installation. Windows OCR's source-language feature must already be available or installed through Windows Settings.
- Creates a Start menu shortcut automatically, with no new Start menu checkbox, and keeps the optional desktop shortcut. Shortcuts are available to all users. At the end the user can open Localay with the original pre-UAC user credentials; silent setup never launches it. If setup is explicitly started from an already elevated process or with “Run as administrator”, Inno cannot recover the original unelevated credentials; open Localay from the Start menu in the intended account instead.
- Subsequent machine-wide installers reuse the same installation identity and folder. An installed higher application file version blocks downgrading. Close Localay in all signed-in accounts before installing or uninstalling; Setup does not kill it or reboot Windows.
- Windows Settings can uninstall Localay. Settings, protected API credentials, profiles and logs remain in `%LOCALAPPDATA%\AlpWare Studio\Localay` across reinstall and uninstall. The installer does not delete that directory.

## Transition from the old per-user setup

The old installer registered under HKCU and installed in `%LOCALAPPDATA%\Programs\Localay`. Inno treats per-user and machine-wide modes as separate installations even with the same AppId. Before this one-time transition, close Localay and uninstall the old copy from Windows Settings in the account that installed it, then run the new setup. User settings and profiles remain in that account's application-data directory; the new setup never copies DPAPI credentials between accounts.

Setup refuses to proceed if it detects a legacy registration in HKCU or another currently loaded user hive. It does not execute a user-writable uninstall command with administrator privileges or delete old registrations. Unloaded user profiles are not inspected: remove old copies from those accounts as well before switching to the machine-wide version.

The Inno loader still has an `asInvoker` manifest: it reads `PrivilegesRequired=admin` and requests elevation itself. `Verify-Installer.ps1` validates the loader structure; `Verify-InstallerPolicy.ps1` validates the installation scope. The app's own manifest remains `asInvoker`.

## Build in GitHub Actions from a Mac

Commit and push the project files, including `installer/`, `assets/`, build/verification scripts, `global.json`, `packages.lock.json` and regression checks. Open **Actions → Build Windows Installer → Run workflow**. The workflow runs only when manually dispatched; pushes and pull requests do not consume build minutes. The default build does not require a signing key or any new repository secret. `scan_with_defender` is an optional manual input, disabled by default; it does not create an additional job or run a live installation test.

A successful run provides:

- **Localay-Setup-windows-x64**: setup EXE, its `.sha256` checksum and compiler/build metadata, including the source commit and Actions run/attempt. Before upload, the workflow checks that the EXE, checksum and metadata still match.
- **Localay-Defender-validation** (when requested): Defender preparation, payload/installer scan reports and scanner logs. Reports/logs are uploaded even if validation fails; a runner failure before reports are written can leave this artifact absent.

GitHub wraps downloaded artifacts in an artifact ZIP. Extract that wrapper once to obtain the single setup EXE distributed to users. The application itself no longer needs to be distributed as a folder of loose runtime files to installer users.

The workflow validates build assets, installer policy and compiler regression cases, installs and verifies the pinned compiler, restores locked/audited dependencies, publishes a self-contained Windows x64 application and builds setup. `Build-Installer.ps1` checks the payload and installer manifest before writing the checksum and build metadata. The workflow also runs startup isolation, preference persistence, OCR scene ownership, translation stop/restart and Windows WPF checks, including the Settings button and full settings loading against temporary configuration. By default it does not run Defender scans. Selecting `scan_with_defender` prepares the disposable hosted runner and scans both the unpacked payload and built installer before uploading the installer. Any preparation/scan failure stops that run from delivering an installer; it is not converted into a success. No install/reinstall/uninstall test is added to the workflow. Installer smoke checks cover Program Files/HKLM registration, legacy conflict and downgrade rejection, automatic Start menu creation, the optional desktop task, reinstall/uninstall and preservation of user data; interactive UAC and original-user launch need a Windows desktop session.

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

Compilation alone is not a malware scan. The optional local `Publish-Windows.ps1` path scans the unpacked application, portable package and installer with Defender and fails if scanning cannot complete. The manual workflow offers optional payload/installer scans without building a portable ZIP. `tests/InstallerSmoke.ps1` stays separate and is not run automatically. Do not describe the workflow artifact as scanned or installation-tested unless the corresponding checks have actually run.

## Interpreting a Windows protection report

A SmartScreen unknown-publisher/reputation warning is different from an antivirus threat detection. The submitted 1.0.0 artifact was unsigned; without the Windows warning text or threat name, the original block cannot be classified conclusively. A clean Defender scan does not establish SmartScreen reputation.

When optional scanning fails, inspect the separate validation artifact and Actions log. A preparation failure means the runner could not scan; a nonzero scanner exit means a scan error or detection, with raw scanner output in the `.log`. The JSON report records failure instead of leaving an old success report. Successful reports bind file hashes to the engine/signature versions and workflow source/run. Do not infer a clean result from the absence of a report.

For an actual named false-positive detection, use Microsoft's [file submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission) with the exact detected file and SHA-256. For distribution trust, sign the app, installer and uninstaller with a trusted Authenticode identity before generating final hashes and running scans. No certificate or signing service is configured here, and self-signing is not a substitute for trusted publisher identity. A trusted signature can reduce warnings over time but cannot guarantee that a new download is accepted on every endpoint.

## Unsigned delivery

The installer, application and generated uninstaller remain unsigned. SmartScreen, Smart App Control, enterprise policies or other antivirus engines can warn or block despite clean scans. CI cannot establish real download reputation or guarantee every endpoint's behavior. A release should accurately describe its available validation rather than promise zero warnings. If a detection occurs, investigate the named file and submit the exact sample to the vendor through its developer false-positive process; do not ask users to disable protection.

References: [Inno installation privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm), [official compiler download](https://jrsoftware.org/isdl.php), [pinned compiler release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3), [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

For opt-in Defender validation on GitHub-hosted Windows runners, `.github/scripts/Prepare-Defender.ps1` handles the [official runner image's Defender configuration](https://github.com/actions/runner-images/blob/main/images/windows/scripts/build/Configure-WindowsDefender.ps1). The scan command and scan-only behavior follow [Microsoft's MpCmdRun documentation](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus).
