; Build only from a verified, self-contained publish directory.
#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Installer.ps1
#endif
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
; Stable identity: keep this AppId across all future versions.
AppId={{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}
AppName=Localay
AppVersion={#AppVersion}
AppPublisher=AlpWare Studio
AppPublisherURL=https://github.com/mahmutaunal/Localay
AppSupportURL=https://github.com/mahmutaunal/Localay/issues
AppUpdatesURL=https://github.com/mahmutaunal/Localay/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Localay Setup
VersionInfoProductName=Localay
DefaultDirName={autopf}\Localay
DefaultGroupName=Localay
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousLanguage=yes
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
WizardStyle=modern dynamic
SetupIconFile={#RepoRoot}\assets\localay.ico
WizardSmallImageFile={#RepoRoot}\assets\icons\localay-256.png
LicenseFile={#RepoRoot}\LICENSE.md
UninstallDisplayIcon={app}\Localay.exe
UninstallDisplayName=Localay
Uninstallable=yes
CreateUninstallRegKey=yes
AppMutex=Local\AlpWareStudio.Localay.Running
SetupMutex=Local\AlpWareStudio.Localay.Setup
; Ask the user to close Localay; never force-kill it or reboot Windows.
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
AlwaysRestart=no
Compression=lzma2/normal
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=Localay-{#AppVersion}-Setup-x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.DesktopShortcut=Create a &desktop shortcut
english.LaunchLocalay=Open Localay
english.LegacyUserInstall=A previous per-user Localay installation was found. Close Localay and uninstall that copy from Windows Settings in its user account, then run this installer again. Your settings and profiles will be preserved.
english.NewerInstalled=A newer version of Localay is already installed. This older installer cannot replace it.
turkish.DesktopShortcut=&Masaüstü kısayolu oluştur
turkish.LaunchLocalay=Localay'i aç
turkish.LegacyUserInstall=Kullanıcıya özel eski bir Localay kurulumu bulundu. Localay’i kapatın, o kullanıcının hesabında Windows Ayarları üzerinden eski kurulumu kaldırın ve bu kurulumu tekrar çalıştırın. Ayarlarınız ve profilleriniz korunur.
turkish.NewerInstalled=Localay'in daha yeni bir sürümü zaten yüklü. Bu eski kurulum dosyasıyla üzerine kurulamaz.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\SECURITY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Localay"; Filename: "{app}\Localay.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Localay.exe"; Description: "{cm:LaunchLocalay}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; No registry writes outside Inno's machine-wide uninstall registration, startup entries,
; services, downloaded prerequisites, shell scripts or deletion of user data.
[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{841CE3F4-7E7E-4BE9-8CED-54A673D319CB}_is1';

function HasLegacyUserInstall: Boolean;
var
  UserKeys: TArrayOfString;
  I: Integer;
begin
  { Read only: never execute an uninstall command from a user-writable registry key.
    Check loaded users too, since UAC may use a different administrator account. }
  Result := RegKeyExists(HKCU, UninstallKey);
  if (not Result) and RegGetSubkeyNames(HKU, '', UserKeys) then
    for I := 0 to GetArrayLength(UserKeys) - 1 do
      if RegKeyExists(HKU, UserKeys[I] + '\' + UninstallKey) then
      begin
        Result := True;
        Exit;
      end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, PackageVersion: Int64;
  InstalledVersionText: String;
  HaveInstalledVersion: Boolean;
begin
  Result := '';
  if HasLegacyUserInstall then
  begin
    Result := CustomMessage('LegacyUserInstall');
    Exit;
  end;
  HaveInstalledVersion := False;
  if RegQueryStringValue(HKLM64, UninstallKey,
       'DisplayVersion', InstalledVersionText) then
    HaveInstalledVersion := StrToVersion(InstalledVersionText, InstalledVersion);
  if not HaveInstalledVersion then
    HaveInstalledVersion := GetPackedVersion(ExpandConstant('{app}\Localay.exe'), InstalledVersion);
  if HaveInstalledVersion and StrToVersion('{#AppVersion}', PackageVersion) then
    if ComparePackedVersion(InstalledVersion, PackageVersion) > 0 then
      Result := CustomMessage('NewerInstalled');
end;
