#define AppName "DB Remote Branch Agent"
#define AppVersion "0.2.0"
[Setup]
AppId={{41BBA256-B157-4F3C-934E-8D551D5883A4}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\DB Solutions\DB Remote Agent
DefaultGroupName=DB Solutions\DB Remote
OutputDir=..\dist
OutputBaseFilename=DBRemote-BranchAgent-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\DBRemote.BranchAgent.exe
[Files]
Source: "..\dist\DBRemote-BranchAgent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\DB Remote Agent"; Filename: "{app}\DBRemote.BranchAgent.exe"
Name: "{autodesktop}\DB Remote Agent"; Filename: "{app}\DBRemote.BranchAgent.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Run]
Filename: "{app}\DBRemote.BranchAgent.exe"; Description: "Launch DB Remote Agent"; Flags: nowait postinstall skipifsilent
