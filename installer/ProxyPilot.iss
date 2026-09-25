#define MyAppName "ProxyPilot"
#define MyAppVersion "1.0.6"
#define MyAppPublisher "daaag0n00969"
#define MyAppURL "https://github.com/daaag0n00969/ProxyPilot"
#define MyAppExeName "ProxyPilot.exe"

[Setup]
AppId={{B4E8A1C2-7D3F-4A90-9E12-6C5B8D0F2A34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
InfoBeforeFile=..\installer\Welcome.txt
OutputDir=..\artifacts
OutputBaseFilename=ProxyPilot-Setup-{#MyAppVersion}-x64
SetupIconFile=..\assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsePreviousAppDir=yes
DisableDirPage=auto
CloseApplications=force
CloseApplicationsFilter=ProxyPilot.exe
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Transparent per-process SOCKS/HTTP proxy for Windows
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\ProxyPilot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\WinDivert.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\WinDivert64.sys"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\WinDivert-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
function GetPreviousVersion(var Version: String): Boolean;
var
  Key: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
  Result := RegQueryStringValue(HKLM64, Key, 'DisplayVersion', Version);
  if not Result then
    Result := RegQueryStringValue(HKLM, Key, 'DisplayVersion', Version);
end;

procedure InitializeWizard;
var
  Prev: String;
begin
  if GetPreviousVersion(Prev) then
  begin
    WizardForm.WelcomeLabel2.Caption :=
      'На этом компьютере уже установлен ProxyPilot ' + Prev + '.' + #13#10 +
      'Установщик обновит его до {#MyAppVersion} в той же папке. Настройки в %AppData%\ProxyPilot сохранятся.' + #13#10 +
      'Запущенный ProxyPilot будет закрыт.' + #13#10#13#10 +
      WizardForm.WelcomeLabel2.Caption;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
