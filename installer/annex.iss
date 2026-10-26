; Annex installer (Inno Setup 6.6+). Build with scripts\build-installer.ps1.
; Per-user install: Annex keeps runtime files (saved password, logs, host profiles) next to Annex.exe,
; so the folder must be owned by the user. Machine-wide settings are applied by scripts\setup.ps1,
; which asks for administrator approval itself.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B0F7C1E-4F2A-4E8B-9C3D-2A5E7B1F0A11}
AppName=Annex
AppVersion={#AppVersion}
AppVerName=Annex {#AppVersion}
AppPublisher=mattxslv
AppPublisherURL=https://github.com/mattxslv/annex
AppSupportURL=https://github.com/mattxslv/annex/issues
DefaultDirName={localappdata}\Programs\Annex
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=AnnexSetup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\Annex.exe
UninstallDisplayName=Annex
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes

[Tasks]
Name: "sunshine"; Description: "Download Sunshine (portable) to stream the Annex desktop"; GroupDescription: "Streaming host:"
Name: "audio"; Description: "Play the Annex desktop's sound only on your streaming device (needs Steam Streaming Speakers)"; GroupDescription: "Streaming host:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\Annex.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\scripts\setup.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\setup-admin.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\install-sunshine.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\annex-autostart.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Annex"; Filename: "{app}\Annex.exe"; WorkingDir: "{app}"; Comment: "Second desktop for game streaming"
Name: "{userdesktop}\Annex"; Filename: "{app}\Annex.exe"; WorkingDir: "{app}"; Comment: "Second desktop for game streaming"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\setup.ps1"" -NoBuild -NoShortcut {code:SetupArgs}"; \
  StatusMsg: "Setting up Windows for Annex (approve the administrator prompts)..."; Flags: runhidden waituntilterminated
Filename: "{app}\Annex.exe"; Description: "Open Annex"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall.ps1"""; \
  RunOnceId: "AnnexUninstall"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Sunshine"
Type: filesandordirs; Name: "{app}\Hosts"
Type: filesandordirs; Name: "{app}\tools"
Type: files; Name: "{app}\*.log"
Type: files; Name: "{app}\cred.bin"
Type: files; Name: "{app}\setup-state.json"
Type: files; Name: "{app}\host-selection.txt"
Type: files; Name: "{app}\child-apps.txt"
Type: files; Name: "{app}\display.cfg"
Type: dirifempty; Name: "{app}"

[Code]
function SetupArgs(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('sunshine') then Result := Result + ' -InstallSunshine';
  if WizardIsTaskSelected('audio') then Result := Result + ' -RouteAudio';
end;
