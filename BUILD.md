# Building Localay

1. Install the .NET 9 SDK on Windows 10/11 x64.
2. Open PowerShell in the repository root.
3. Run `dotnet restore .\Localay.csproj -r win-x64`.
4. Run `dotnet build .\Localay.csproj -c Release --no-restore` (output: `bin/Release`).
5. Run `./Publish-Windows.ps1` for verified self-contained output in `artifacts/publish`.

The Windows TFM supplies the Windows SDK projections and WinRT runtime. Do not add the CsWinRT projection generator solely to consume Windows APIs; it adds a machine-installed Windows SDK dependency that this application does not need.

The Windows GitHub Actions workflow builds Release, publishes win-x64, verifies runtime/architecture/assets and the absence of retired speech dependencies, then uploads a portable ZIP and SHA-256 checksum. Pushes to main or codex branches, pull requests and manual dispatch trigger it. Build failures upload an MSBuild binary log.

Speech recognition and TTS engines, downloaders and controls have been removed. Historic configuration keys remain readable for existing profiles; no speech engine uses them. Capture, OCR, translation, overlay and chat history remain in place.

Localay targets Windows APIs and cannot be runtime-tested on macOS/Linux. A Windows interactive smoke test of capture and overlays is still required before publishing a binary release.
