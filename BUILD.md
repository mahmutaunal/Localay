# Building Localay

Use the .NET 10 LTS SDK selected by `global.json`. The normal release target is Windows x64, using the Windows 10 19041 API surface; test on supported Windows versions before release. PowerShell release scripts require PowerShell 7.

```powershell
dotnet restore ./Localay.csproj -r win-x64 --locked-mode
dotnet build ./Localay.csproj -c Release -r win-x64 --no-restore
./Verify-SecurityPolicy.ps1
dotnet run --project ./tests/SecurityChecks/SecurityChecks.csproj -c Release
./installer/Install-Compiler.ps1
# The installer script sets LOCALAY_ISCC in the current PowerShell session.
./Publish-Windows.ps1
```

`Publish-Windows.ps1` clears the generated `artifacts/publish` directory, publishes the multi-file .NET application, verifies its standard-user manifest and distribution contents, and produces both the optional portable ZIP and the main per-user setup EXE. It updates Defender signatures and scans the raw payload and both distribution formats, failing if validation cannot complete. Output: `artifacts/package` for the ZIP, `artifacts/installer` for `Localay-<version>-Setup-x64.exe` and checksum. The compiler must be installed and selected with `LOCALAY_ISCC`; GitHub Actions handles this automatically using the hash/signature-verified pinned official compiler. Nothing is signed by this project. See [installer/README.md](installer/README.md) for exact workflow/artifact instructions.

The **Build Windows Installer** GitHub Actions workflow runs only through `workflow_dispatch`. It validates build assets, installer policy and compiler regression cases, installs the pinned compiler, restores locked dependencies, publishes the application, and builds/verifies the setup EXE with its checksum and metadata. It does not run Defender, WPF integration or installer lifecycle checks. No physical Windows machine is required to trigger this from a Mac. Package version and displayed application version come from `Localay.csproj`. Update `global.json` to a serviced .NET 10 SDK and regenerate `packages.lock.json` intentionally when updating dependencies. NuGet vulnerability warnings for direct and transitive dependencies fail the restore/build.

The default build uses Windows OCR; install its source-language feature in Windows Settings. OneOCR files are excluded from the standard package. See `SECURITY.md` for optional asset licensing, pinned hashes and signature checks before a custom redistribution.

The Windows TFM supplies SDK projections and WinRT runtime. Do not add CsWinRT projection generation solely to consume Windows APIs.

## Cross-platform validation

On macOS/Linux, `dotnet build ./Localay.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true` can check Windows compilation. `dotnet run --project ./tests/SecurityChecks/SecurityChecks.csproj -c Release` exercises portable migration/redaction/restart-argument checks; DPAPI and WPF integration tests run on Windows only. A Windows self-contained executable cannot be runtime-tested on macOS/Linux. Cross-compilation and package checks do not imply Defender, SmartScreen, capture or overlay validation. Follow the interactive release checks in `SECURITY.md` before public distribution.

Installer policy checks also run on macOS in PowerShell 7: `./tests/InstallerPolicyChecks.ps1`. Installer compilation runs on Windows in CI; lifecycle checks require a separate Windows run of `tests/InstallerSmoke.ps1`. No local cross-build should be described as a completed installer run or Defender scan.

UI regression checks also run on macOS/Linux:

```powershell
dotnet run --project ./tests/PreferenceChecks/PreferenceChecks.csproj -c Release
dotnet run --project ./tests/LocalizationChecks/LocalizationChecks.csproj -c Release
```

The localization check validates English/Turkish key parity, duplicate/empty values, format placeholders, XAML/code resource references, language names and hardcoded UI text. Technical logs, model/provider identifiers and external error details retain their original language. The pinned SDK supplies the source parser; no additional parser package is downloaded. These source checks do not exercise WPF bindings or layout.

After the Windows Release build, validate branding, language-specific product links and the exact license documents embedded in Localay.dll:

```powershell
dotnet run --project ./tests/BrandChecks/BrandChecks.csproj -c Release -- . ./bin/Release/Localay.dll
```

This check inspects the Windows assembly as data, so it also runs on macOS/Linux without launching WPF or opening any external links.

## Combined validation

Keep `assets/localay.ico` and `assets/icons/localay-256.png` in version control. The application and installer reference these files directly; the README logo does not replace them. `tests/BuildAssetChecks.ps1` verifies these inputs before compilation and packaging.

Run `./Verify-Project.ps1` in PowerShell 7 for a locked/audited restore, Windows Release build, source/installer policies, security, preferences, localization and product/license checks. On Windows it also runs `tests/UiWindowsChecks`, an STA WPF host that exercises live language changes, production language templates, theme brushes, About and API-key dialogs. It does not start Localay's capture loop, create user settings, register hotkeys, or open links. On macOS/Linux that runtime check is explicitly recorded as `not_run`.

`artifacts/validation/project/summary.json` records passed, failed and not-run steps, with one log per check. Independent checks continue after a failure; the command exits unsuccessfully if any step fails. `-SkipRestore` is for repeated checks with existing restored assets and is explicitly reported as skipping the fresh audit. The manual installer workflow does not invoke this combined validation command or upload these reports; run it separately when that broader validation is required.

To cross-compile the Windows WPF check on macOS, use `dotnet build ./tests/UiWindowsChecks/UiWindowsChecks.csproj -c Release -p:EnableWindowsTargeting=true`. Compilation does not execute that check.

Release validation also checks that published English/Turkish resources exactly match the source files. The publish regression suite rejects missing language files and stale but valid Turkish JSON, in addition to private data and an incorrect runtime.
