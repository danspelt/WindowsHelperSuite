#define MyAppName "Windows Helper Suite"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Dan Spelt"
#define MyAppExeName "WindowsHelperSuite.App.exe"

[Setup]
AppId={{A9A5C0F1-53F0-4D6D-8E2A-1D1E0A6F1001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\release
OutputBaseFilename=WindowsHelperSuite-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Uncomment after adding icon files:
; SetupIconFile=..\assets\icons\installer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\src\WindowsHelperSuite.App\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
