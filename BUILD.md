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

Run `./Verify-Project.ps1` in PowerShell 7 for a locked/audited restore, Windows Release build, source/installer policies, security, preferences, OCR scene regressions, localization and product/license checks. On Windows it also runs `tests/UiWindowsChecks`, an STA WPF host that exercises live language changes, production language templates, theme brushes, About and API-key dialogs, and the complete compiled Settings window (all categories, ignore-phrase bindings, hide/reopen and redacted error details). The Settings visual check skips runtime service/profile initialization; actual profile-loading failures and the main-window Settings button still require an interactive Windows check. It does not start Localay's capture loop, create user settings, register hotkeys, or open links. On macOS/Linux that runtime check is explicitly recorded as `not_run`.

`artifacts/validation/project/summary.json` records passed, failed and not-run steps, with one log per check. Independent checks continue after a failure; the command exits unsuccessfully if any step fails. `-SkipRestore` is for repeated checks with existing restored assets and is explicitly reported as skipping the fresh audit. The manual installer workflow does not invoke this combined validation command or upload these reports; run it separately when that broader validation is required.

To cross-compile the Windows WPF check on macOS, use `dotnet build ./tests/UiWindowsChecks/UiWindowsChecks.csproj -c Release -p:EnableWindowsTargeting=true`. Compilation does not execute that check.

Release validation also checks that published English/Turkish resources exactly match the source files. The publish regression suite rejects missing language files and stale but valid Turkish JSON, in addition to private data and an incorrect runtime.

The portable OCR scene checks run with `dotnet run --project ./tests/OcrSceneChecks/OcrSceneChecks.csproj -c Release`. They exercise text/geometry identity, scrolling, OCR jitter, empty scenes, latest-result buffering and the translation ownership latch. They do not run Windows OCR or render overlays. On Windows, manually translate a scrolling launcher page and confirm that old boxes/background patches disappear when OCR detects movement, repeated text is repositioned without another translation call, and only the newest deferred OCR result is applied after a slow translation. Also verify an empty page, duplicate labels, capture/language changes and auto-OCR disabled.

Request lifetime checks run with `dotnet run --project ./tests/LifetimeChecks/LifetimeChecks.csproj -c Release` and are included in `Verify-Project.ps1`. They use fake providers and an in-memory HTTP handler to test session invalidation, native HTTP cancellation, uncooperative providers, request isolation, reused text IDs and stale prefetch/cache writes (including stop/restart with identical settings). No real provider or network is used. Interactive Windows checks must still cover stop/start, switching applications/areas/languages while OCR or a slow translation is pending, and verify that canceled work produces no new overlays, history entries or error dialogs.

Translation resilience checks run with `dotnet run --project ./tests/TranslationResilienceChecks/TranslationResilienceChecks.csproj -c Release` and are included in `Verify-Project.ps1`. They exercise the production request context, HTTP diagnostics handler, Google free client, separator mapping, cache and retry policy with in-memory responses. No real translation service is contacted. Missing translations remain empty slots, never substituted source text; combined response counts must match before positional assignment. A partial result caches only populated slots; missing slots leave the underlying application text visible rather than drawing a source-text overlay or an eraser patch. Per unchanged scene, foreground failures allow at most two automatic retries (after 2 and 5 seconds); HTTP 429 waits at least 30 seconds, and a longer Retry-After takes precedence. Authentication, access-denied and invalid-request errors require manual intervention. Source/session changes and explicit Retry reset this allowance. Existing provider-internal endpoint retries and prefetch are separate from this foreground allowance.

On Windows, manually verify network loss/recovery on unchanged text, a rate-limited service, an invalid paid-service key, partial/malformed responses, a successful Google fallback and stopping/changing the area during failure recovery. Check that missing parts do not appear as translated source text, successful parts stay aligned, automatic retries stop, and intermediate fallback failures/canceled requests produce no error dialogs. These checks do not establish exclusive-fullscreen capture support; a fullscreen failure must still be reproduced with capture/OCR and provider diagnostics distinguished.

Overlay font/layout checks run with `dotnet run --project ./tests/OverlayLayoutChecks/OverlayLayoutChecks.csproj -c Release` and are included in `Verify-Project.ps1`. They test source line/column metrics, preserving heading/body size groups during merging, invalid/reversed font limits, shrink policy and pixel-to-DIP conversions at 100/125/150/175/200% scaling. OCR paragraphs retain the median actual line height (column width for vertical text) through later merging. If those metrics are absent, explicit line count/source newlines and box extent provide a fallback. Source glyph widths in the translation font no longer determine the font estimate. Normal placement keeps that estimate and tries free width/wrapping first; only the final viewport-overflow pass may shrink, within the configured minimum. Actual source-size differences, configured bounds and the final fallback can still produce different sizes.

The Windows WPF suite additionally exercises production TextObject measurement with short/long Turkish labels, headings, multiline paragraphs, RTL, fixed sizing, custom limits, the Loaded callback, and reprojecting an existing box/font/eraser patch after DPI changes. Immutable rendering options let these checks run without creating application settings. These are logical layout checks, not physical-monitor emulation. Existing capture objects retain pixel geometry and reproject on the WPF Window.DpiChanged event as well as overlay refresh; repeated monitor changes do not repeatedly scale already-converted coordinates. The event follows the Windows rendering DPI ([Microsoft documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.dpichanged?view=windowsdesktop-10.0)).

For manual Windows acceptance, compare Ubisoft Launcher's headings and body labels while scrolling, including long Turkish translations and an edge-of-screen selection. At 100/125/150/200% scaling, check equal source-line sizes, intentional heading/body differences, wrapping, collision placement and the configured minimum. Move the capture overlay between monitors with different scaling while a translation is already visible: boxes, source erasure and text must stay aligned without another translation request. Test RTL/vertical CJK and auto-size disabled separately. The original game's exact typeface is not available from OCR, so this remains a size estimate rather than exact font reproduction. No Windows display-language change is needed.

OCR language setup is available through Settings > translation languages > Check / install OCR, and opens automatically if Start is blocked by an unavailable source-language recognizer. For an English source translated into Turkish, the required OCR feature is English; Turkish OCR is only relevant when the SOURCE text is Turkish. Installing these features does not set the Windows display language. The guide shows the selected feature language, the recognizers actually available to Windows OCR, recheck, Windows Optional features and a manual command-copy option.

On an explicit Install click, Localay stays a standard-user application and starts the system Windows PowerShell executable with Windows UAC for that operation only. The compiled, allow-listed command loads the system DISM module, queries actual Windows capabilities, checks that both Basic and OCR are offered, then adds only missing Basic/OCR features in dependency order through Windows Update. It uses the capability names/versions returned by Windows. It changes no display-language/user-language preferences, installs no display-language UI pack and never automatically restarts Windows. UAC cancellation, unavailable capabilities, failures, Install Pending and restart-required results remain distinct. Completion is rechecked against the WinRT recognizer list; Windows feature completion alone does not assert that OCR is already usable. Closing the guide leaves an ongoing Windows servicing process running. After completion, close the guide and press Start again. Organization policies or Windows Update connectivity can still prevent installation.

Installed regional variants are accepted for the application's generic source-language choices; Chinese scripts stay separate. The OCR engine uses the matching installed recognizer rather than a user-profile-language fallback. An OCR API activation failure is shown separately from a missing package. A mapping is a capability query target, not a guarantee that a particular Windows version supplies OCR for that language.

`tests/OcrSetupChecks` covers language matching, capability input validation, launch arguments, exit results, all source choices and dynamic English/Turkish guide messages. `tests/OcrSetupScriptChecks.ps1` parses the generated production command, verifies its command allow-list and runs it against a fake DISM module for install order, idempotency, missing/ambiguous support, pending/restart, errors and non-installation. These checks never execute real Windows servicing. Both are in `Verify-Project.ps1`; the latter requires a PowerShell process without an already-loaded DISM module. The WPF suite adds guide localization, progress/duplicate-click guards, refreshed availability, failure states and closing during a fake operation. On macOS it is compiled only; actual UAC, Windows PowerShell 5.1/DISM, download and post-reboot recognition need Windows acceptance testing. Test on a disposable Windows 11 VM with a non-English display language, including a missing English OCR feature, already-installed Basic, UAC cancellation, restricted/offline Windows Update and Install Pending. Confirm the display language before and after remains unchanged. Do not remove a real user's OCR features just to create this test state.

References: [Microsoft OCR setup guidance](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor), [Basic/OCR dependencies](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-language-fod?view=windows-11), [Add-WindowsCapability and Windows Update](https://learn.microsoft.com/en-us/powershell/module/dism/add-windowscapability?view=windowsserver2025-ps).
