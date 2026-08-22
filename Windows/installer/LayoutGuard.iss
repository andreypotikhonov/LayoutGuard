#define MyAppName "LayoutGuard"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Andrei Potikhonov"
#define MyAppExeName "LayoutGuard.exe"

[Setup]
AppId={{A1E86148-1C86-42D4-BF70-71D50DAB8519}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=LayoutGuard-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LayoutGuard"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\LayoutGuard"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить LayoutGuard"; Flags: nowait postinstall skipifsilent
