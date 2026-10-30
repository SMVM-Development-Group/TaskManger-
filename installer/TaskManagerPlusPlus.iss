#define AppName "TaskManager++"
#define AppVersion "1.0.0"
#define AppPublisher "TaskManager++"
#define AppExeName "TaskManagerPlusPlus.exe"

[Setup]
AppId={{A1B2C3D4-5E6F-7A8B-9C0D-E1F2A3B4C5D6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\TaskManagerPlusPlus
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=C:\Users\glute\TaskManger-\build\installer
OutputBaseFilename=TaskManagerPlusPlus-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayName={#AppName}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "C:\Users\glute\TaskManger-\build\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  // App is self-contained (SelfContained=true), no .NET runtime check needed.
  Result := True;
end;
