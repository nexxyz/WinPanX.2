#define MyAppName "WinPan X.2"
#define MyAppExeName "WinPanX2.exe"
#define PublishExe "..\\src\\WinPanX2\\bin\\Release\\net8.0-windows\\win-x64\\publish\\WinPanX2.exe"

#if FileExists(PublishExe)
  #define MyAppVersion GetVersionNumbersString(PublishExe)
#else
  #define MyAppVersion "1.4.1.0"
#endif
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

#define ArchFlag "x64compatible"
ArchitecturesInstallIn64BitMode={#ArchFlag}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\src\WinPanX2\bin\Release\net8.0-windows\win-x64\publish\*"; \
    DestDir: "{app}"; \
    Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\WinPan X.2"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\WinPan X.2"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create Desktop Icon"; GroupDescription: "Additional options:"
Name: "startup"; Description: "Launch WinPan X.2 on Windows startup"; GroupDescription: "Additional options:"; Flags: checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WinPan X.2"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "WinPanX2"; ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: startup; Flags: uninsdeletevalue
