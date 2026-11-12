; Instalador do Flow Frames Brasil (Inno Setup 6)
; Compile com:  ISCC.exe /DAppVersion=1.0.0 FlowFramesBrasil.iss
; (o script Build\build.ps1 faz tudo automaticamente)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef StageDir
  #define StageDir "..\Build\stage"
#endif

#define AppName "Flow Frames Brasil"
#define AppExe  "FlowFramesBrasil.exe"
#define AppUrl  "https://github.com/leaomoacir/flowframes-brasil"

[Setup]
AppId={{8D1B7C52-4F0E-4B8A-9C3D-5E2A6F1B7A94}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Flow Frames Brasil
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Instalacao por usuario: nao exige administrador e permite atualizar em silencio
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\Build\out
OutputBaseFilename=FlowFramesBrasil-Setup-{#AppVersion}
SetupIconFile=..\CodeLegacy\FlowframesLogo2021.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Instalacao normal: oferece abrir o programa ao final
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent
; Atualizacao automatica (iniciada pelo proprio app com /RELAUNCH=1): reabre o programa
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
Type: filesandordirs; Name: "{app}\FlowframesData"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
