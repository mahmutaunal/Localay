param([Parameter(Mandatory)][string]$ScriptPath)
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $ScriptPath -Raw
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw "Installer PowerShell failed to parse: $parseErrors" }
$allowed = @('Import-Module', 'Join-Path', 'Dism\Get-WindowsCapability', 'Dism\Add-WindowsCapability', 'Write-Error', 'Where-Object')
foreach ($command in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
    if ($null -eq $command.GetCommandName() -and $command.CommandElements.Count -eq 1 -and
        $command.CommandElements[0] -is [Management.Automation.Language.ScriptBlockExpressionAst] -and
        $command.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand) { continue }
    if ($command.GetCommandName() -notin $allowed) { throw "Unexpected installer command: $($command.GetCommandName())" }
}
$compiled = [scriptblock]::Create($source)
$script:passed = 0
function Check([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "FAILED: $Message" }
    $script:passed++
    Write-Host "PASS: $Message"
}
# The ONLY servicing commands available to the tested script are these fake module functions.
# Do not invoke the installed Windows DISM module, processes, network, registry or preferences.
if (Get-Module -Name Dism) { throw 'Run these mock checks in a clean PowerShell process without a loaded DISM module.' }
$mock = New-Module -Name Dism -ScriptBlock {
    function Get-WindowsCapability {
        [CmdletBinding()] param([switch]$Online, [string]$Name)
        if (-not $Online) { throw 'Expected online capability query' }
        foreach ($key in $global:LocalayOcrTestStates.Keys) {
            if (-not $Name -or $key -like $Name) { [pscustomobject]@{ Name = $key; State = $global:LocalayOcrTestStates[$key] } }
        }
    }
    function Add-WindowsCapability {
        [CmdletBinding()] param([switch]$Online, [string]$Name)
        if (-not $Online -or -not $global:LocalayOcrTestStates.ContainsKey($Name)) { throw 'Unexpected add' }
        $global:LocalayOcrTestAdds.Add($Name)
        if ($global:LocalayOcrTestMode -eq 'Throw') { throw 'Simulated Windows Update failure' }
        if ($global:LocalayOcrTestMode -eq 'NoChange') { return [pscustomobject]@{ RestartNeeded = $false } }
        $global:LocalayOcrTestStates[$Name] = if ($global:LocalayOcrTestMode -in @('Restart', 'Pending')) { 'InstallPending' } else { 'Installed' }
        [pscustomobject]@{ RestartNeeded = ($global:LocalayOcrTestMode -eq 'Restart') }
    }
    Export-ModuleMember -Function Get-WindowsCapability, Add-WindowsCapability
}
Import-Module $mock -Force
foreach ($name in @('Dism\Get-WindowsCapability', 'Dism\Add-WindowsCapability')) {
    $command = Get-Command $name
    if ($command.CommandType -ne 'Function' -or -not [object]::ReferenceEquals($command.Module, $mock)) {
        throw 'A real servicing command would be reachable. Refusing to run mock checks.'
    }
}
# Validate the production import path without loading an actual servicing module.
function Import-Module {
    [CmdletBinding()] param([string]$Name)
    if ($Name -ne (Join-Path $PSHOME 'Modules\Dism\Dism.psd1')) { throw 'Untrusted module path' }
}
$basic = 'Language.Basic~~~en-US~0.0.1.0'
$ocr = 'Language.OCR~~~en-US~0.0.1.0'
function Scenario([hashtable]$States, [string]$Mode = 'Normal') {
    $global:LocalayOcrTestStates = $States
    $global:LocalayOcrTestAdds = [Collections.Generic.List[string]]::new()
    $global:LocalayOcrTestMode = $Mode
    $code = @(& $compiled 2>$null)
    if ($code.Count -ne 1 -or $code[0] -isnot [int]) { throw "Expected one integer result; got $code" }
    return $code[0]
}
try {
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'NotPresent' }
    Check ($code -eq 0 -and $global:LocalayOcrTestAdds.Count -eq 2 -and $global:LocalayOcrTestAdds[0] -eq $basic -and $global:LocalayOcrTestAdds[1] -eq $ocr) 'Basic is installed before OCR, and both final states are verified'
    $code = Scenario @{ $basic = 'Installed'; $ocr = 'NotPresent' }
    Check ($code -eq 0 -and $global:LocalayOcrTestAdds.Count -eq 1 -and $global:LocalayOcrTestAdds[0] -eq $ocr) 'already installed Basic is preserved'
    $code = Scenario @{ $basic = 'Installed'; $ocr = 'Installed' }
    Check ($code -eq 0 -and $global:LocalayOcrTestAdds.Count -eq 0) 'repeating setup does not reinstall completed features'
    $code = Scenario @{ $basic = 'NotPresent' }
    Check ($code -eq 11 -and $global:LocalayOcrTestAdds.Count -eq 0) 'missing OCR support is detected before installing Basic'
    $code = Scenario @{ $ocr = 'NotPresent' }
    Check ($code -eq 11 -and $global:LocalayOcrTestAdds.Count -eq 0) 'missing Basic dependency does not attempt an OCR install'
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'InstallPending' }
    Check ($code -eq 14 -and $global:LocalayOcrTestAdds.Count -eq 0) 'pending Windows servicing is not treated as completed or modified again'
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'NotPresent' } 'Restart'
    Check ($code -eq 10 -and $global:LocalayOcrTestAdds.Count -eq 1) 'a Basic restart request postpones OCR until after restart'
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'NotPresent' } 'Pending'
    Check ($code -eq 14 -and $global:LocalayOcrTestAdds.Count -eq 1) 'Install Pending after adding Basic never reports success'
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'NotPresent' } 'Throw'
    Check ($code -eq 12 -and $global:LocalayOcrTestAdds.Count -eq 1) 'Windows Update errors return a failure without continuing to OCR'
    $code = Scenario @{ $basic = 'Installed'; $ocr = 'NotPresent' } 'NoChange'
    Check ($code -eq 12) 'a command returning without installing OCR is not a successful setup'
    $newBasic = $basic.Replace('0.0.1.0', '0.0.2.0'); $newOcr = $ocr.Replace('0.0.1.0', '0.0.2.0')
    $code = Scenario @{ $newBasic = 'NotPresent'; $newOcr = 'NotPresent' }
    Check ($code -eq 0 -and $global:LocalayOcrTestAdds[0] -eq $newBasic -and $global:LocalayOcrTestAdds[1] -eq $newOcr) 'capability versions come from Windows rather than a guessed fixed version'
    $code = Scenario @{ $basic = 'NotPresent'; $ocr = 'NotPresent'; $newOcr = 'NotPresent' }
    Check ($code -eq 11 -and $global:LocalayOcrTestAdds.Count -eq 0) 'ambiguous capability results are rejected without system changes'
    Write-Host "$script:passed mocked OCR servicing scenarios passed; PowerShell parsed and command allow-list verified. No real servicing was performed."
} finally {
    Remove-Module Dism -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\Import-Module -ErrorAction SilentlyContinue
    Remove-Variable LocalayOcrTestStates, LocalayOcrTestAdds, LocalayOcrTestMode -Scope Global -ErrorAction SilentlyContinue
}
