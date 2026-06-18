; Inno Setup script for Terminal Shortcuts (Command Palette extension)
; COM registration uses LocalServer32 for ExeServer (not InprocServer32).

#define AppVersion "0.1.0.0"
#define ExtensionName "TerminalShortcuts"
#define DisplayName "Terminal Shortcuts"
#define DeveloperName "Tony Thompson"
#define Clsid "{A3FFFE73-298E-4749-BE32-BFD576F0E3FF}"

[Setup]
AppId={{8C4E2F91-6B3D-4A5E-9F1C-2D7E8A0B4C6D}}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher={#DeveloperName}
AppPublisherURL=https://github.com/tonythethompson/CmdPalTerminalShortcuts
DefaultDirName={autopf}\{#ExtensionName}
OutputDir=bin\Release\installer
OutputBaseFilename={#ExtensionName}-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041
PrivilegesRequired=lowest
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#DisplayName}"; Filename: "{app}\{#ExtensionName}.exe"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{#Clsid}"; ValueType: string; ValueName: ""; ValueData: "{#ExtensionName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{#Clsid}\LocalServer32"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ExtensionName}.exe"" -RegisterProcessAsComServer"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
