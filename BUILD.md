# Building Localay

1. Install the .NET 9 SDK on Windows 10/11 x64.
2. Open PowerShell in the repository root.
3. Run `dotnet restore .\Localay.csproj`.
4. Run `dotnet build .\Localay.csproj -c Release`.
5. For a self-contained build run `dotnet publish .\Localay.csproj -c Release -r win-x64 --self-contained true`.

Localay targets Windows APIs and cannot be meaningfully runtime-tested on macOS/Linux. A Windows smoke test is required before publishing a binary release.
