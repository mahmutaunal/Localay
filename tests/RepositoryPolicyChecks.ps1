$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $privatePaths = @(
        'bin/Release/Localay.dll', 'tests/BrandChecks/bin/Release/app.exe', 'tests/BrandChecks/obj/project.assets.json',
        'Localay-1.0.0-windows-x64.zip', 'artifacts/installer/Localay-Setup.exe', 'artifacts/validation/result.json', 'dist/package.zip',
        'config.txt', 'gemini_config.txt', 'custom_api_config.txt', 'config_backup_20261020.txt',
        'app/config.txt', 'app/chatgpt_config.txt', 'app/ollama_config.txt', 'app/Profiles/private.txt',
        'Profiles/private.txt', 'Logs/Content/last_ocr_response.json', 'last_llm_request_sent.txt',
        '.env', '.env.production', 'signing.pfx', 'credentials.local.json', 'secrets.json',
        '.DS_Store', 'app/.DS_Store', 'downloaded.exe', 'downloaded.dll', 'models/model.gguf'
    )
    $sourcePaths = @(
        'Localay.csproj', 'global.json', 'packages.lock.json', 'app.manifest',
        'src/MainWindow.xaml', 'src/MainWindow.xaml.cs', 'app/Languages/tr.json', 'app/Languages/en.json',
        'app/OneOcr/oneocr.dll', 'app/OneOcr/oneocr.onemodel', 'app/README.md',
        'assets/localay.ico', 'assets/icons/localay-256.png', 'assets/localay-logo-master.png',
        'installer/Localay.iss', 'installer/compiler.json', 'security/native-assets.json', 'security/ONEOCR-LICENSE.txt',
        'docs/guide.md', 'mockups/design.md', 'tests/fixtures/settings.json', '.env.example',
        '.github/workflows/windows-installer.yml', '.github/scripts/Prepare-Defender.ps1',
        '.vscode/extensions.json', 'LICENSE.md', 'THIRD_PARTY_NOTICES.md', 'Verify-Project.ps1'
    )
    foreach ($path in $privatePaths + $sourcePaths) {
        & git check-ignore --no-index --quiet -- $path
        $code = $LASTEXITCODE
        if ($code -notin @(0, 1)) { throw "git check-ignore failed for $path (exit $code)." }
        $expectedIgnored = $path -in $privatePaths
        if (($code -eq 0) -ne $expectedIgnored) { throw "Unexpected ignore policy for $path (expected ignored=$expectedIgnored)." }
    }
    $ignoredTracked = @(& git ls-files --cached --ignored --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect tracked ignored files.' }
    if ($ignoredTracked.Count) { throw "Files are already tracked despite ignore rules: $($ignoredTracked -join ', '). Review their index entries explicitly." }
    Write-Host "$($privatePaths.Count + $sourcePaths.Count) repository ignore checks passed; no tracked files conflict with ignore rules."
} finally { Pop-Location }
