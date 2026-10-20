# Security and unsigned distribution

Localay is distributed as an unsigned Windows x64 setup EXE, with an optional portable ZIP. Neither a successful build nor a clean malware scan guarantees SmartScreen reputation, Smart App Control approval, or acceptance by every antivirus product. Do not ask users to disable Defender, SmartScreen, Smart App Control, or add blanket exclusions.

## Application behavior

- Runs as the current user (`asInvoker`), without elevation or UIAccess.
- Captures the user-selected window through Windows Graphics Capture; keeps Windows' capture indicator visible. No game-process injection, memory patching, global keyboard/mouse hooks, or key polling.
- Global shortcuts use `RegisterHotKey`. Another application's reservation, a higher-integrity window, or game/anti-cheat policies can prevent a shortcut or overlay from working. Use the on-screen controls and windowed/borderless mode where appropriate.
- No automatic update checks or downloads. The Official releases button opens `https://github.com/mahmutaunal/Localay/releases` in the user's browser. Update an installed copy by closing Localay and running the newer setup EXE. Portable users extract the complete new package into a clean folder.
- Restart launches the same application in a helper mode that waits up to 60 seconds for its parent, verifies the parent's identity and then starts the application. It does not use a shell, PowerShell, batch files, or downloaded programs.

## Installer

The short per-user Inno Setup installer uses `PrivilegesRequired=lowest`, includes its entire offline payload and creates only user shortcuts and normal HKCU uninstall registration. It does not elevate, download executables, configure startup, change security settings or recursively delete user data. Application/setup mutexes prevent replacement of a running app; no force-kill or reboot is used. Settings stay outside the installation directory and survive reinstall/uninstall. Inno's standard installer and uninstaller are unsigned along with the application. See [installer/README.md](installer/README.md) for compiler pinning, workflow instructions and automated installer checks.

## User data and privacy

Settings, profiles and logs are in `%LOCALAPPDATA%\AlpWare Studio\Localay`. Windows DPAPI protects API keys and legacy signature credentials for the current user. This protects storage, not credentials while used by that user's running applications. Copying encrypted settings to another account/PC may require re-entering credentials. Shared profiles omit credentials and do not replace existing account credentials when imported. Reset backups also contain protected credentials.

On first launch, known legacy configuration and profile files beside the application are imported without overwriting existing user data. Main configuration credentials are encrypted before writing the destination. After a successful import, plaintext credentials are removed from the original main configuration where permissions allow. Old manually created backups and old logs are not deleted or imported; review and remove those before sharing an old application folder. A read-only legacy folder cannot be sanitized without additional access; Localay does not elevate to modify it.

Clipboard translation is off by default. Enabling it requires a privacy explanation and consent; older settings without this consent are reset to off. When enabled, copied text from any application can be sent to the selected provider, and optional auto-copy can replace clipboard contents. Stop monitoring in Settings. Cloud translation sends text to the selected provider; local providers only remain local while their endpoint is configured to a local service.

Translation content diagnostics are off by default and require consent in the Log window for each session. While enabled, captured text, prompts, clipboard translations and provider responses may appear in the log or `%LOCALAPPDATA%\AlpWare Studio\Localay\Logs\Content`. Known credentials and common URL/bearer tokens are redacted, but review diagnostics before sharing: redaction is not a guarantee that provider responses contain no other private data. Disabling diagnostics deletes their disk snapshots; starting a later session clears previous snapshots. Ordinary status/crash logs can still contain technical information; inspect them before sharing.

## Native OCR

The standard distribution uses Windows OCR and does not redistribute OneOCR binaries/models. Install the Windows OCR language feature for the source language through Windows Settings if it is missing. The source tree retains optional OneOCR integration and inherited assets, but redistribution permission and original package provenance are not established here.

A custom OneOCR build requires `security/ONEOCR-LICENSE.txt` containing the applicable redistribution authorization and license, plus `-p:IncludeOneOcrAssets=true`. Merely adding a file does not grant permission. Review this independently before distributing. Approved builds verify the pinned SHA-256 values in `security/native-assets.json` and require a valid Microsoft Authenticode signature on Windows. The runtime rejects unrecognized OneOCR assets and restricts native library lookup to the application directory and System32. Hash pinning detects changes relative to this repository; it does not establish origin or license rights.

## Release checks

Use PowerShell 7 on a clean Windows x64 machine with Microsoft Defender Antivirus available:

1. Run `./Verify-SecurityPolicy.ps1`.
2. Run `dotnet run --project ./tests/SecurityChecks/SecurityChecks.csproj -c Release` (includes DPAPI, migration, profile import/export and reset regression checks on Windows).
3. Prepare the pinned compiler with `./installer/Install-Compiler.ps1` and run `./Publish-Windows.ps1` (locked dependency restore with vulnerability audit, clean self-contained publish, embedded manifest/package checks, installer/ZIP checksum generation, raw payload and final package Defender scans). An unavailable scanner or failed scan blocks validation; do not label that output scanned.
4. For download-reputation/interactive validation, download the exact setup EXE (or portable ZIP) from its intended HTTPS release location using a browser and test setup and first launch under a standard account on clean Windows 11, including a device with Smart App Control enabled. Preserve the normal download-origin information for this test.
5. Test window capture, required OCR language pack, overlay, shortcuts, clipboard consent, reset/restart and manual update. Confirm that normal operation does not request elevation or write settings into the application directory. Repeat important games/anti-cheat configurations separately.
6. Record setup EXE/ZIP SHA-256, Windows build, Defender engine/signature versions, observed SmartScreen/SAC prompts and detection names. A local or CI scan does not test real download reputation. Test additional antivirus products in appropriate isolated environments if they are part of the release requirements.

The workflow uploads packages only after automated checks and Defender scan succeed. CI restores scanning on its disposable GitHub-hosted Windows VM by removing the image's inherited disk exclusions and enabling archive scanning. It then automatically tests install, same-version reinstall, uninstall and preservation of a user-data sentinel and scans the installed payload/uninstaller. Scan reports contain file hashes and require unchanged files after a scan-only check. It does not reproduce interactive UI/game behavior, a physical standard-user device or browser download reputation. Unsigned updates have a different file hash and may need to establish reputation again. A checksum distributed beside a ZIP checks consistency; it is not proof of publisher identity if both are replaced.

For an actual false positive, submit the exact detected binary with product/detection details as a software developer to the affected vendor. Do not change files merely to evade a detection. Microsoft references:

- [SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [Developer false-positive FAQ](https://learn.microsoft.com/en-us/defender-xdr/developer-faq)
- [Microsoft file submission](https://www.microsoft.com/en-us/wdsi/filesubmission)
- [.NET support and self-contained runtime maintenance](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
