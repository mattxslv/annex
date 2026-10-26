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
ClickNext=
StatusRunProgram=Setting up Windows for Annex...
UninstallAppTitle=Remove Annex
UninstallAppFullTitle=Remove Annex
ConfirmUninstall=Remove Annex?%n%nThis signs out the Annex desktop, removes Annex and its Sunshine, and puts your Windows settings back the way they were before Annex. Windows asks once for administrator approval.
UninstallStatusLabel=Removing Annex and restoring your Windows settings...
UninstalledAll=Annex was removed and your Windows settings were restored.
UninstalledMost=Annex was removed. A few files were still in use; you can delete the Annex folder in %LOCALAPPDATA%\Programs yourself.

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
Source: "..\scripts\setup-common.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
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
// Header: the same margin above the logo, to its right, and left of the title (the page content's
// left inset). The header is margin + logo + margin tall; title and description are centred against
// the logo. Re-applied on page changes because Inno lays the header out again.
const
  LogoSize = 72;      // logo box; the visible mark spans x 2..62 and y 8..56 of a 64-unit grid
  PanelAdjust = 0;    // header panel offset from the page edge, measured

procedure LayoutHeader(Panel: TPanel; NameLabel, DescLabel: TNewStaticText; Logo: TBitmapImage; Notebook: TNewNotebook; Bevel: TBevel; M: Integer);
var
  S, VisTop, VisHeight, VisRight, Want, Grow, DescOffset, TextHeight, TextTop: Integer;
begin
  S := ScaleY(LogoSize);
  VisTop := S * 8 div 64; VisHeight := S * 48 div 64; VisRight := S * 2 div 64;
  Want := M + VisHeight + M;
  Grow := Want - Panel.Height;
  if Grow <> 0 then begin
    Panel.Height := Want;
    Notebook.Top := Notebook.Top + Grow;
    Notebook.Height := Notebook.Height - Grow;
    Bevel.Top := Bevel.Top + Grow;
  end;
  Logo.Stretch := True;
  Logo.SetBounds(Panel.ClientWidth - S + VisRight - M + ScaleX(PanelAdjust), M - VisTop, S, S);
  DescOffset := DescLabel.Top - NameLabel.Top;
  TextHeight := DescOffset + DescLabel.Height;
  TextTop := M + (VisHeight - TextHeight) div 2;
  NameLabel.SetBounds(M, TextTop, Logo.Left - M - ScaleX(16), NameLabel.Height);
  DescLabel.SetBounds(M, TextTop + DescOffset, NameLabel.Width, DescLabel.Height);
end;

procedure PlaceHeader;
begin
  LayoutHeader(WizardForm.MainPanel, WizardForm.PageNameLabel, WizardForm.PageDescriptionLabel, WizardForm.WizardSmallBitmapImage,
    WizardForm.InnerNotebook, WizardForm.Bevel1, WizardForm.InnerNotebook.Left + WizardForm.SelectTasksLabel.Left);
end;

// The uninstaller gets the same header.
procedure InitializeUninstallProgressForm;
begin
  LayoutHeader(UninstallProgressForm.MainPanel, UninstallProgressForm.PageNameLabel, UninstallProgressForm.PageDescriptionLabel,
    UninstallProgressForm.WizardSmallBitmapImage, UninstallProgressForm.InnerNotebook, UninstallProgressForm.Bevel1,
    UninstallProgressForm.InnerNotebook.Left + UninstallProgressForm.StatusLabel.Left);
end;

// Welcome and finish pages: the heading starts below the top of the banner, text follows it.
// The banner bitmap is drawn centred (not stretched) in a page-high control, so its visible top
// edge is below the control's top.
procedure PlaceBannerText(Heading, Body: TNewStaticText);
var
  BannerTop: Integer;
begin
  BannerTop := WizardForm.WizardBitmapImage.Top + (WizardForm.WizardBitmapImage.Height - ScaleY(314)) div 2;
  Heading.Top := BannerTop + ScaleY(24);
  // The heading box is sized for two lines; shrink it so it doesn't cover the text below.
  Heading.Height := ScaleY(34);
  Body.Top := Heading.Top + Heading.Height + ScaleY(12);
  // Finish page: keep the "Open Annex" checkbox below the text.
  if Body = WizardForm.FinishedLabel then
    WizardForm.RunList.Top := Body.Top + Body.Height + ScaleY(16);
end;

procedure InitializeWizard;
begin
  PlaceHeader;
  PlaceBannerText(WizardForm.WelcomeLabel1, WizardForm.WelcomeLabel2);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  PlaceHeader;
  if CurPageID = wpWelcome then PlaceBannerText(WizardForm.WelcomeLabel1, WizardForm.WelcomeLabel2);
  if CurPageID = wpFinished then PlaceBannerText(WizardForm.FinishedHeadingLabel, WizardForm.FinishedLabel);
end;

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
