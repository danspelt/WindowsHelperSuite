; Inno Setup 6 — packages dotnet publish output into a per-user installer.
; Prerequisites: run scripts/publish-release.ps1 (or dotnet publish with Win64SelfContained profile), then compile this script.
;
; Install Inno Setup: https://jrsoftware.org/isinfo.php
; Build installer: ISCC.exe WindowsHelperSuite.iss (from this folder)

#define MyAppName "Windows Helper Suite"
#define MyAppExeName "WindowsHelperSuite.exe"
; Keep in sync with WindowsHelperSuite.App.csproj <Version>
#define MyAppVersion "1.2.1"
#define MyAppPublisher "Dan Spelt"
; Stable AppId — do not change after first public release (upgrades/uninstall depend on it)
#define MyAppId "{{A7F3C8D1-4E2B-5A6C-9D0E-1F2A3B4C5D6E}"
; Relative to this .iss file (installer\)
#define PublishDir "..\src\WindowsHelperSuite.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Dans.minme/WindowsHelperSuite
; Per-user install (no elevation): same pattern as many modern desktop apps
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=WindowsHelperSuiteSetup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Optional:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WindowsHelperSuite"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data under %AppData%\WindowsHelperSuite is intentionally NOT removed.
