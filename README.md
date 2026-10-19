# Localay

**Real-time game localization for Windows** — an open-source AlpWare Studio project.

Localay captures a game window through Windows Graphics Capture, detects visible text, translates it, and renders the result as an overlay. The goal is to make subtitles, menus, tutorials, quests, item descriptions and other readable UI text understandable without modifying game files or injecting code into the game process.

## Design goals

- Windows-first and game-focused.
- Zero monetary cost for normal use.
- Low overhead: frame-change detection, translation caching and bounded real-time processing avoid unnecessary work.
- No DLL injection, memory patching or game-file modification.
- English → Turkish defaults, while the inherited language/provider system remains available.
- In-place presentation where supported: OCR bounding boxes, block detection and text erasing are used to keep translated text close to the original UI.

## Quick start

### Requirements

- Windows 10 2004 (build 19041) or newer; Windows 11 recommended.
- x64 PC.
- .NET 9 SDK only when building from source. Published self-contained builds do not require a separate .NET installation.

### Build

```powershell
dotnet restore .\Localay.csproj
dotnet build .\Localay.csproj -c Release
```

### Publish

```powershell
./Publish-Windows.ps1
```

The verified portable output is written to `artifacts/publish`; Release build output is isolated in `bin/Release`. Language files and OneOCR assets are copied into both outputs.

The project intentionally keeps the proven RSTGameTranslation WPF/Direct3D capture and overlay architecture instead of rewriting the UI. Localay defaults to English source text and Turkish output.

## Translation modes and cost

The default provider is the no-key Google Translate web mode inherited from RSTGameTranslation, so Localay is usable without paid API credentials. It requires internet access. For fully local/offline translation, the inherited **Ollama** and **LM Studio** providers can be used with a locally installed model; this has no per-request cost but uses more RAM/CPU/GPU depending on the model.

Localay does not claim that every game or every stylized font can be localized perfectly. OCR accuracy, overlays, exclusive fullscreen modes, anti-cheat policies, HDR and animated UI can vary by game. Borderless/windowed mode is recommended when a game blocks overlays.

## Performance notes

Localay inherits and preserves the performance-oriented parts of the upstream engine, including Windows Graphics Capture/Direct3D 11 capture, frame-change detection, translation caching, OCR timing controls, spatial block detection and stale-result protections. The speech recognition and TTS stack is removed from Localay: Whisper, CUDA/Vulkan speech runtimes, Sherpa, NAudio, System.Speech and the speech-only ONNX runtime are not shipped. The OCR, translation, capture and overlay UI is retained; retired speech controls are removed.

## Privacy

Screen processing happens on the local PC. Text is sent to a network service only when a network translation provider is selected. Ollama/LM Studio modes keep translation local.

## Open source and attribution

Localay is a GPL-3.0 derivative of **RSTGameTranslation**. The original GPL license and attribution are preserved. DeskTranslate and OCR Overlay were reviewed as architectural references; their source trees are not bundled into this repository.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.
