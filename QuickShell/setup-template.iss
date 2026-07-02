; Inno Setup script for Quick Shell (Command Palette extension)
; COM registration uses LocalServer32 for ExeServer (not InprocServer32).

#define AppVersion "0.1.6.0"
#define ExtensionName "QuickShell"
#define DisplayName "Quick Shell"
#define DeveloperName "Tony Thompson"
#define Clsid "528cc766-cbe8-4861-9933-722c7a3f3581"

#define InstallAppId "8C4E2F91-6B3D-4A5E-9F1C-2D7E8A0B4C6D"
#define RunPluginSource "..\QuickShell.Run\bin\x64\Release\package"
#define RunPluginDest "{localappdata}\Microsoft\PowerToys\PowerToys Run\Plugins\QuickShell"

[Setup]
AppId={{{#InstallAppId}}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher={#DeveloperName}
AppPublisherURL=https://github.com/tonythethompson/QuickShell
DefaultDirName={localappdata}\Programs\{#ExtensionName}
DisableDirPage=yes
DisableProgramGroupPage=yes
AlwaysUsePersonalGroup=yes
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
Source: "{#RunPluginSource}\*"; DestDir: "{#RunPluginDest}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#DisplayName}"; Filename: "{app}\{#ExtensionName}.exe"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{{#Clsid}}}"; ValueType: string; ValueName: ""; ValueData: "{#ExtensionName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{{#Clsid}}}\LocalServer32"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ExtensionName}.exe"" -RegisterProcessAsComServer"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
