$ErrorActionPreference = "Stop"
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "app.manifest")
$execution = $manifest.SelectSingleNode("//*[local-name()='requestedExecutionLevel']")
if ($execution.level -ne "asInvoker" -or $execution.uiAccess -ne "false") {
    throw "Localay must run without forced elevation or UIAccess."
}
$forbidden = 'SetWindowsHookEx|GetAsyncKeyState|ExecutionPolicy|DownloadAndInstallUpdate|CreateUpdateScript|WriteProcessMemory|CreateRemoteThread'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "src") -Filter *.cs) {
    if ((Get-Content -LiteralPath $file.FullName -Raw) -match $forbidden) {
        throw "Review required: disallowed input-monitoring or execution mechanism in $($file.Name)."
    }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Localay.csproj")
if (@($project.Project.PropertyGroup.TargetFramework | Where-Object { $_ })[0] -notlike 'net10.0-windows*') {
    throw "The application must use the supported .NET 10 LTS target."
}
Write-Host "Verified standard-user execution and absence of hooks, polling, shell bypass and self-update scripts."
