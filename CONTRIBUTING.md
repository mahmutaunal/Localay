# Contributing to Localay

Keep changes aligned with Localay's core goal: fast, lightweight Windows game localization.

Prefer improvements to capture efficiency, OCR accuracy, change detection, caching, layout fitting and overlay quality over adding unrelated features. Avoid game-process injection, memory patching and mandatory paid/cloud dependencies.

Before submitting changes, build the Release configuration on Windows and test at least one borderless/windowed game with English source text and Turkish output.

Keep source, documentation, application assets, installer sources and `packages.lock.json` in version control. Build/publish output belongs under ignored `bin/`, `obj/` or `artifacts/` directories. Do not commit Localay user settings, API keys, personal prompts, profiles or diagnostic captures; this includes legacy `config.txt` and `*_config.txt` files in the repository root or `app/`. The normal application stores user data outside the checkout.

Run `./tests/RepositoryPolicyChecks.ps1` in PowerShell 7 to verify that private/generated paths are ignored and required source paths remain visible. `./Verify-Project.ps1` and GitHub Actions include this check. `.gitignore` does not remove files already committed; inspect staged changes before publishing.
