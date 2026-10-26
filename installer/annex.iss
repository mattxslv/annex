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
WizardStyle=modern dark
WizardImageFile=wizard-100.bmp,wizard-200.bmp
WizardSmallImageFile=small-100.bmp,small-200.bmp
WizardImageStretch=no
DisableWelcomePage=no
DisableReadyPage=yes
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes

[Messages]
WelcomeLabel1=Welcome to Annex
WelcomeLabel2=Annex gives your PC a second Windows desktop for streaming games and apps to your phone, tablet or TV, while you keep using this one.%n%nSetup installs Annex for your Windows account and prepares Windows for it. Windows will ask twice for administrator approval.
WizardSelectTasks=Choose what to set up
SelectTasksDesc=Pick the extras you want. Annex itself is always installed.
SelectTasksLabel2=You can install Sunshine later from inside Annex too.
FinishedHeadingLabel=Annex is ready
FinishedLabelNoIcons=Open Annex and choose Start desktop. The first time, it asks for your Windows password (not your PIN).
FinishedLabel=Open Annex and choose Start desktop. The first time, it asks for your Windows password (not your PIN).
ClickFinish=
StatusRunProgram=Setting up Windows for Annex...

[Tasks]
Name: "sunshine"; Description: "Download Sunshine for the Annex desktop (recommended, about 36 MB)"; GroupDescription: "Streaming"
Name: "audio"; Description: "Play its sound only on your phone, tablet or TV (needs Steam's audio driver)"; GroupDescription: "Streaming"; Flags: unchecked
Name: "desktopicon"; Description: "Add an Annex shortcut to the desktop"; GroupDescription: "Shortcuts"

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
// Nothing is installed before the Installing page, so Cancel just closes without asking.
procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if CurPageID <> wpInstalling then Confirm := False;
end;

function SetupArgs(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('sunshine') then Result := Result + ' -InstallSunshine';
  if WizardIsTaskSelected('audio') then Result := Result + ' -RouteAudio';
end;
