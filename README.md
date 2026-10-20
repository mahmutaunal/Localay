<p align="center">
  <img src="assets/logo.png" alt="Localay logo" width="112">
</p>

<h1 align="center">Localay</h1>

<p align="center">
  Real-time game localization for Windows.<br>
  An open-source project by <a href="https://www.alpwarestudio.com/en/products/">AlpWare Studio</a>.
</p>

<p align="center">
  <a href="https://github.com/mahmutaunal/Localay/releases">Downloads</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#build-from-source">Build from source</a> ·
  <a href="https://github.com/mahmutaunal/Localay/issues">Report an issue</a>
</p>

Localay captures text from a game window or a selected screen area, recognizes it with OCR, and translates it into your chosen language. Read the result over the game or in a separate ChatBox window—without modifying game files or injecting code into the game process.

![Localay English interface preview](assets/en.png)

*Representative preview of the English interface in light theme; not an actual Windows screenshot.*

## Features

- **Translate visible game text:** subtitles, dialogue, menus, quests, tutorials, and item descriptions.
- **Choose your capture area:** select an application window or a specific screen region.
- **Read your way:** display translations in an overlay or a separate ChatBox.
- **Choose your languages:** English → Turkish is the default; source and target languages can be changed.
- **Follow your system preferences:** automatic interface language and theme selection, with manual overrides in Settings.
- **Use cloud or local translation:** start with the default provider or configure a locally hosted model.
- **Keep controls within reach:** configurable shortcuts, reusable profiles, and optional clipboard translation.
- **Reduce repeated work:** frame-change detection and translation caching limit unnecessary processing.

## Requirements

- Windows 10 version 2004 (build 19041) or newer; Windows 11 recommended.
- An x64 PC.
- The Windows OCR language feature for the language you want to recognize.
- An internet connection for online translation, or a configured local translation service and model for offline use.

Published builds include the .NET runtime. You do not need to install .NET separately to run Localay. Available recognition and translation languages depend on the OCR engine and translation provider.

## Installation

### Windows installer

Download `Localay-<version>-Setup-x64.exe` from the latest release on [GitHub Releases](https://github.com/mahmutaunal/Localay/releases), then run it to install Localay.

The short setup installs Localay for the current user without requesting administrator privileges. It adds a Start menu shortcut and offers an optional desktop shortcut. You can uninstall it through Windows Settings; user settings and credentials are retained.

To get a build directly from GitHub Actions:

1. Open **Actions → Build Windows Installer** in the [GitHub repository](https://github.com/mahmutaunal/Localay/actions) and select a successful run.
2. Download the **Localay-Setup-windows-x64** artifact.
3. Extract the downloaded archive, then run the setup EXE inside it.

See the [installer guide](installer/README.md) for artifact contents, checksums, and installation validation.

### Portable version

The **Localay-Portable-windows-x64** Actions artifact contains the portable package. Extract the package into a folder and launch `Localay.exe`. Keep all included files together.

## Quick start

1. **Choose your languages.** Set **Source Language** to the language visible in the game and **Target Language** to the language you want to read. Source language selection is manual.
2. **Choose what to capture.** Use **Select Application** for a game window, or **Select Area** for a specific screen region.
3. **Start translating.** Choose **Overlay** or **ChatBox**, then start translation from the main window.

Use **More → Settings** to change the translation provider, interface language, theme, capture behavior, and shortcuts. On Windows 10, overlay mode requires a selected application window.

OCR accuracy depends on text size, contrast, font, and animation. Compatibility can also vary with HDR, exclusive fullscreen, and game protection policies. Try borderless or windowed mode if capture or overlays do not work as expected.

## Translation providers

| Option | Setup | Connection and cost |
| --- | --- | --- |
| Google Translate web mode (default) | No API key required | Internet required; subject to service availability and limits. |
| Cloud API providers | Configure the provider and your credentials in Settings | Internet required; provider pricing and usage limits apply. |
| Ollama or LM Studio | Install a model and configure the local service | Can work offline once the model is installed; hardware requirements depend on the model. |

Localay does not require a paid translation subscription. Optional API services may charge for usage. Local models use your PC's resources and may need additional RAM or GPU memory.

## Privacy and security

Screen capture and OCR run on your PC. When you choose an online translation provider, recognized text and configured context are sent to that provider. Use Ollama or LM Studio with a locally hosted model to keep translation local.

- Clipboard translation is disabled by default and requires consent.
- Logging translation content requires a separate opt-in for each session.
- Settings are stored in `%LOCALAPPDATA%\AlpWare Studio\Localay`.
- API keys are protected for the current Windows user and excluded from shared profiles.

Localay is currently distributed without code signing. Windows SmartScreen, Smart App Control, antivirus software, or organizational policies may warn about or block an unsigned build. Release validation includes Microsoft Defender scanning, but a clean scan cannot guarantee that every security product will accept the executable. See [SECURITY.md](SECURITY.md) for release checks and false-positive reporting.

### Updates

Localay does not automatically download or replace executables. Close the app and run the newer installer to update it while preserving settings. For portable installations, extract the new package into a clean folder.

## Build from source

Use the .NET SDK selected by [global.json](global.json). PowerShell 7 is required for the validation and packaging scripts. Windows is required to run the application and validate the installer.

```powershell
dotnet restore ./Localay.csproj -r win-x64 --locked-mode
dotnet build ./Localay.csproj -c Release -r win-x64 --no-restore
```

Run the project checks from PowerShell 7:

```powershell
./Verify-Project.ps1
```

For setup and portable packages, use the **Build Windows Installer** GitHub Actions workflow. You can trigger it from a Mac through **Actions → Build Windows Installer → Run workflow**. The workflow builds on Windows and validates the packages before uploading artifacts.

See [BUILD.md](BUILD.md) for local build, publishing, and validation instructions, and the [installer guide](installer/README.md) for packaging details.

## Contributing

Bug reports and focused improvements are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a pull request.

For bugs, open an [issue](https://github.com/mahmutaunal/Localay/issues) with your Windows version, Localay version, selected OCR and translation providers, and steps to reproduce the problem. Remove API keys and private content from logs or screenshots. Follow [SECURITY.md](SECURITY.md) for security-related reports.

## License and acknowledgments

Localay is licensed under [GNU GPL v3.0](LICENSE.md) and is derived from **RSTGameTranslation** by Thanh Pham. Original attribution and license notices are preserved.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies and acknowledgments. License information is also available offline in **More → About Localay → Licenses and contributors**.

## AlpWare Studio

Developed by **AlpWare Studio**.

- [Explore our other projects](https://www.alpwarestudio.com/en/products/)
- [Source code on GitHub](https://github.com/mahmutaunal/Localay)
- [contact@alpwarestudio.com](mailto:contact@alpwarestudio.com)
