; Inno Setup script for Quick Shell for PowerToys Run (standalone)

#define AppVersion "0.0.0.0"
#define DisplayName "Quick Shell for Run"
#define InstallerBaseName "QuickShellforRun"
#define DeveloperName "Tony Thompson"
#define PluginSource "__MUST_BE_SET_BY_BUILD_SCRIPT__"
#define PluginDest "{localappdata}\Microsoft\PowerToys\PowerToys Run\Plugins\QuickShell"

[Setup]
AppId={{B7D2C4E8-9F1A-4D6B-A2C3-5E8F1D0B7A9C}}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher={#DeveloperName}
AppPublisherURL=https://github.com/tonythethompson/QuickShell
DefaultDirName={localappdata}\QuickShell\Run
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=bin\Release\installer
OutputBaseFilename={#InstallerBaseName}-Setup-{#AppVersion}-PLATFORM
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041
PrivilegesRequired=lowest
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PluginSource}\*"; DestDir: "{#PluginDest}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Messages]
FinishedLabel=Quick Shell for Run was installed to %n%n{#PluginDest}%n%nRestart PowerToys so Run picks up the qs plugin.

[UninstallDelete]
Type: filesandordirs; Name: "{#PluginDest}"
