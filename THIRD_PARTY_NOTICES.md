# Third-party notices

## RSTGameTranslation

Localay is derived from RSTGameTranslation and retains substantial portions of its Windows capture, OCR, translation, overlay and UI implementation. The upstream project is distributed under the GNU General Public License v3.0. Its license text is retained in `LICENSE.md`.

Original developer attribution retained from the upstream application: Thanh Pham. Localay is developed and maintained by AlpWare Studio.

Upstream project: https://github.com/thanhkeke97/RSTGameTranslation

## DeskTranslate

DeskTranslate (MIT) was reviewed as a reference for real-time pipeline, temporal processing and bounded-work design. No DeskTranslate source tree is vendored in Localay.

Upstream project: https://github.com/DeskTranslate/DeskTranslate

## OCR Overlay

OCR Overlay (MIT) was reviewed as a reference for OCR/layout/rendering architecture. No OCR Overlay source tree is vendored in Localay.

Upstream project: https://github.com/mrAntiUser/ocr_overlay

## NuGet and runtime dependencies

The project uses third-party NuGet packages and Windows runtime components declared in `Localay.csproj`. Their respective licenses apply to those components.

## OneOCR (optional, not in standard distributions)

The repository inherited `app/OneOcr/oneocr.dll` and `oneocr.onemodel` alongside the OneOCR integration. The original Microsoft package/version and authorization for separate redistribution are not documented in the inherited material. This project does not assert that its GPL license grants rights to redistribute those native assets. Standard packages therefore exclude them and use Windows OCR supplied by Windows.

`security/native-assets.json` records SHA-256 baselines of the inherited files. These hashes establish neither license rights nor original provenance. A custom build must supply applicable redistribution authorization/license as `security/ONEOCR-LICENSE.txt`, explicitly enable `IncludeOneOcrAssets`, and pass hash and Windows Authenticode checks. See `SECURITY.md`.

## Managed packages

Direct packages: Hardcodet.NotifyIcon.Wpf 2.0.1, Newtonsoft.Json 13.0.4, System.Management 10.0.0 and System.Security.Cryptography.ProtectedData 10.0.0. `packages.lock.json` records transitive versions and NuGet package content hashes. NuGet packages retain their own upstream license terms; the bundled .NET runtime's license and third-party notices are included by the SDK.

## Inno Setup installer engine

The Windows setup EXE and uninstaller are generated with Inno Setup 6.7.3. The compiler is obtained from the official release with pinned download hash and publisher-signature checks; the full compiler is not bundled with Localay. Inno Setup's own upstream terms apply to its installer engine. See [upstream license](https://github.com/jrsoftware/issrc/blob/is-6_7_3/license.txt) and `installer/compiler.json` for the exact release source.
