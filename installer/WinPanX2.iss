#define MyAppName "WinPan X.2"
#define MyAppExeName "WinPanX2.exe"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WinPan"

[Setup]
AppId={{C6A8A6B7-7E44-4B4A-9F7A-0C8F6D8F3F21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\WinPan X.2
DefaultGroupName=WinPan X.2
AllowNoIcons=yes

OutputDir=Output
OutputBaseFilename=WinPanX2-Setup
Compression=lzma
SolidCompression=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

SetupIconFile=..\src\WinPanX2\Assets\WinPanX.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\src\WinPanX2\bin\Release\net8.0-windows\win-x64\publish\WinPanX2.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\WinPan X.2"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\WinPan X.2"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create Desktop Icon"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WinPan X.2"; Flags: nowait postinstall skipifsilent
