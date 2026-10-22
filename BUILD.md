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

`Publish-Windows.ps1` clears the generated `artifacts/publish` directory, publishes the multi-file .NET application, verifies its standard-user manifest and distribution contents, and produces both the optional portable ZIP and the main administrator-required setup EXE. It updates Defender signatures and scans the raw payload and both distribution formats, failing if validation cannot complete. Output: `artifacts/package` for the ZIP, `artifacts/installer` for `Localay-<version>-Setup-x64.exe` and checksum. The compiler must be installed and selected with `LOCALAY_ISCC`; GitHub Actions handles this automatically using the hash/signature-verified pinned official compiler. Nothing is signed by this project. See [installer/README.md](installer/README.md) for exact workflow/artifact instructions.

The **Build Windows Installer** GitHub Actions workflow runs only through `workflow_dispatch`. It validates build assets, installer policy and compiler regression cases, installs the pinned compiler, restores locked dependencies, publishes the application, and builds/verifies the setup EXE with its checksum and metadata. It runs startup isolation and Windows WPF checks, including the actual Settings command with temporary configuration. Defender scanning is optional and off by default; installer lifecycle checks are not run. No physical Windows machine is required to trigger this from a Mac. Package version and displayed application version come from `Localay.csproj`. Update `global.json` to a serviced .NET 10 SDK and regenerate `packages.lock.json` intentionally when updating dependencies. NuGet vulnerability warnings for direct and transitive dependencies fail the restore/build.

The default build uses Windows OCR; install its source-language feature in Windows Settings. OneOCR files are excluded from the standard package. See `SECURITY.md` for optional asset licensing, pinned hashes and signature checks before a custom redistribution.

The Windows TFM supplies SDK projections and WinRT runtime. Do not add CsWinRT projection generation solely to consume Windows APIs.

The manual **Build Windows Installer** workflow keeps Defender scanning optional (`scan_with_defender`, off by default). Enabling it scans the published payload and installer on the disposable hosted runner and uploads separate diagnostics; a failed requested scan prevents the installer artifact from being uploaded. No live installation test is added. Build metadata records the source commit and Actions run/attempt, and the workflow verifies the installer/checksum identity immediately before upload. See [installer/README.md](installer/README.md) for the distinction between antivirus detections and unsigned-download reputation warnings.

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

Run `./Verify-Project.ps1` in PowerShell 7 for a locked/audited restore, Windows Release build, source/installer policies, security, preferences, OCR scene regressions, localization and product/license checks. On Windows it also runs `tests/UiWindowsChecks`, an STA WPF host that exercises live language changes, production language templates, theme brushes, About and API-key dialogs, and the complete compiled Settings window (all categories, ignore-phrase bindings, hide/reopen and redacted error details). The markup-only Settings check skips runtime initialization; a separate command check enables full settings/profile loading with temporary configuration and exercises the main-window button and profile failure recovery. Physical capture and foreground/DPI behavior still require an interactive Windows check. It does not start Localay's capture loop, create user settings, register hotkeys, or open links. On macOS/Linux that runtime check is explicitly recorded as `not_run`.

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

## Startup and Settings lifecycle

`dotnet run --project ./tests/StartupChecks/StartupChecks.csproj -c Release` verifies that a failing optional startup phase reports its original exception and does not skip later phases. `Verify-Project.ps1` includes this portable check.

Language selection and the bound version label are initialized before capture, animation or other optional features. Loaded phases are isolated and logged by name; partial startup shows a localized warning. A repeated Loaded event does not attach the same runtime handlers again.

On Windows, `dotnet run --project ./tests/UiWindowsChecks/UiWindowsChecks.csproj -c Release` also opens the production MainWindow markup and invokes its Settings button against a temporary ConfigManager. The full Settings loading path is enabled. Capture, clipboard monitoring, global hotkeys and welcome prompts are disabled in this host. Checks cover the initial language/version, ownership, ten close/reopen cycles, single field commits after reloads, minimized/hidden/off-screen windows, and recovery from a temporary profile directory conflict. Temporary configuration and subscriptions are cleaned up. This automated check is part of the installer workflow before packaging; it still does not prove physical monitor/DPI behavior, native foreground activation on a user's desktop, or the original reported failure on their machine.

## Translation stop/restart ownership

Each foreground translation owns a unique scene operation ID. Stop, capture/settings resets and observed text changes invalidate that owner before canceling the old pipeline. The busy state is released immediately, so a new request can start even if an old provider ignores cancellation. Only the matching owner may finish the UI status and drain deferred OCR; old dispatcher callbacks also check the scene generation. Presentation-only scene resets do not release a still-valid owner. Completion always releases ownership even if result notification throws.

`OcrSceneChecks` covers cancellation, owner isolation and deferred generations. `LifetimeChecks` combines the production lifetime, scene state and request wrapper in 50 stop/restart cycles with providers that ignore cancellation. Old results and cleanup must not block, release or overwrite new work, consume its OCR or produce a cancellation error. Both checks run before packaging in the Windows installer workflow. The capture timer skips work while stopped; restarting clears hashes and the one-shot latch before rearming OCR. Windows OCR disposes its native SoftwareBitmap after recognition, including missing-engine/error paths. Real screen capture, OS OCR and user interaction still require the final Windows validation.

## Interface preference persistence

Language and theme changes are written to an atomic replacement file before updating the in-memory preference or applying it to the UI. Failure retains the last committed values, reports the existing save error, and synchronizes MainWindow, Settings and Quick Start selectors back to that choice. Unrelated later saves cannot persist a failed selection. The `system` preference remains `system` on disk; resolving a Windows language does not replace it.

Game profile export/import excludes `language_interface`, `theme_mode` and legacy `dark_mode`, in addition to existing private values. Original legacy main settings retain these interface preferences when their credentials are sanitized; imported game profiles do not carry them.

Localization builds a complete dictionary before changing its effective language. Missing, malformed or null language files fall back to English; missing/blank/null individual translations use English values. Binding observers are notified independently so an observer exception cannot revert the dictionary or prevent other windows from updating. Preference snapshots are recorded after applying the language/theme, allowing a failed resource load to be retried on a later refresh.

Run `dotnet run --project ./tests/PreferencePersistenceChecks/PreferencePersistenceChecks.csproj -c Release` for portable persistence, filesystem failure, profile exclusion, resource fallback and observer isolation checks. The Windows WPF suite additionally changes language through MainWindow, Settings and Quick Start, reads the persisted choice with a fresh ConfigManager, and forces an actual configuration-file replacement failure to verify rollback in all three selectors. These use isolated temporary data and a captured save-error reporter. They do not simulate an entire OS/application restart. The portable check is included in combined validation and the installer workflow.


## Pipeline timing diagnostics (P0)

The opt-in pipeline recorder tracks capture/OCR, settling, provider work, UI application and partial/failed/canceled outcomes using operation-scoped monotonic clocks. Normal use allocates no detailed traces or report files. Set `LOCALAY_PIPELINE_DIAGNOSTICS=1` before launching Localay; pause/stop exports a bounded, content-free numeric JSON report to the existing Logs directory.

Run `dotnet run --project ./tests/PipelineTimingChecks/PipelineTimingChecks.csproj -c Release` to validate the production timing/ownership helpers and twelve synthetic stage sequences. Repeat with the diagnostics environment variable enabled to exercise asynchronous provider scope and late completion. The default-mode check is included in `Verify-Project.ps1`. Use `--report <path>` to export synthetic samples, `--replay <report.json>` to recompute statistics, or `--benchmark` to measure recorder overhead only. See [the timing guide](tests/PipelineTimingChecks/README.md) for exact commands and report definitions.

The optimization target is now preferably below 100 ms with preserved quality and low resource usage. P0 does not change capture frequency or select a translation model. Its live interval starts at the application's capture entry and ends at logical WPF application, not at source appearance or physical screen presentation. Those endpoints and semantic correctness are explicitly marked `not_measured`; synthetic replay and cross-compilation do not establish Windows OCR/display latency.


## Capture frame ownership (P1)

WGC arrival keeps one newest native frame and releases superseded pending surfaces without per-event bitmap conversion. A bounded drain uses the native pool capacity. Capture/OCR is single-flight; the capturing caller keeps both the original bitmap and any masked copy alive until awaited OCR/result processing finishes. OCR no longer disposes its caller's image. Blocking OCR preprocessing runs off the UI thread; result handling retains the existing UI/session guards. Static, settled WGC frames avoid repeated cached-image clones except when settling/retry/periodic recheck needs them.

The existing `LifetimeChecks` executable covers frame replacement, ownership transfer, shutdown, bounded draining and capture gate isolation. No new installer workflow tests were added; the P0-only additions were removed at the user's request. Local Windows cross-builds and physical Windows testing are not part of this workflow; Windows builds stay in the existing GitHub Actions pipeline. These portable checks verify ownership/concurrency contracts, not measured screen latency.


### Regional freshness and block matching (P2)

The existing capture path now observes RGB changes while one native OCR operation is in flight. Each input keeps a bounded tile damage map; stale OCR regions and their background patches cannot be presented by a late response. Stationary blocks can remain visible. Exact-text block matching preserves identities across OCR ordering changes; repeated labels require corroborated, unambiguous movement. Unknown response IDs are no longer treated as list positions.

Active WGC freshness polling uses a 33 ms timer; cached settling/retry observations and GDI retain a 250 ms cadence. This is a polling interval, not a measured OCR/translation/presentation latency. Regional OCR and rendering cost remain later plan items.

Focused validation uses the existing projects, with no new workflow steps or dependencies:

```sh
dotnet run --project tests/OcrSceneChecks/OcrSceneChecks.csproj -c Release --no-restore
dotnet run --project tests/LifetimeChecks/LifetimeChecks.csproj -c Release --no-restore
```

P2 results: 66 scene checks and 34 lifetime checks passed. No downloads or local Windows builds were performed.


### Missing-block translation and shared requests (P3)

Screen foreground/prefetch translation now share per-context source units. Already translated objects are retained; only missing units use ID-tagged batches (maximum 15 units per provider call, one actual call at a time, 256 pending units). Reply ownership is validated by requested IDs and optional original source metadata. Normal screen translation no longer packs the scene into ID `999` with application separators. Yandex/Microsoft use their array APIs for the new protocol; literal separators inside a source block remain content.

Google keeps its 1000 ms dispatch spacing and server cooldown. Canceled waiting calls do not reserve future dispatch slots. Managed screen batches use sequential configured fallback rather than speculative parallel fallback; a slow Google request may therefore reach fallback later than the previous 1.5-second hedge.

Relevant portable checks are shared by the existing `LifetimeChecks` and `TranslationResilienceChecks` projects. No workflow steps or packages were added. This session could validate XML/source paths and whitespace, but could not execute C# checks: the previously temporary .NET SDK is absent. No SDK download or local Windows build was attempted. P3 test/compilation status remains **not run**, and no end-to-end latency result is claimed.


### Stable rendering and OCR resource reuse (P4)

Windows OCR reuses its language engine and pixel buffer, moving preprocessed BGRA pixels directly into SoftwareBitmap rather than encoding/decoding BMP. WGC retains one staging texture per active size/format; OneOCR avoids a redundant BGRA redraw and releases image locks on failure.

OCR visuals are prepared as a set, unchanged source visuals can retain their Border/eraser, and normal refresh no longer clears/re-adds the canvas. New visuals are fitted with the real WPF DPI while hidden. Placement preserves source anchors and protects neighboring source boxes; it no longer pushes translations down. Unreadable/overlapping fits leave the original source visible and keep their full translation in ChatBox. Known neighboring text is excluded from eraser mask growth.

Portable resource/placement/mask checks share the existing LifetimeChecks/OverlayLayoutChecks projects; focused WPF reuse checks extend UiWindowsChecks. No workflow step or dependency was added. XML/source paths and whitespace checks passed. Runtime/compiler checks remain **not run** because the local .NET SDK is absent. Regional cropped OCR remains disabled pending boundary/composition/quality validation; no measured latency improvement is claimed.
