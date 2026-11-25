; ProjectToolkit installer (Inno Setup 6.3 or later). Design: docs/design/tdd/installer.md (INS-1 to INS-24).
; Build: ISCC.exe /DAppVersion=<version> /DSourceDir=<staged tree> [/O<output dir>] [/DTest] [/DSign] ProjectToolkit.iss
; The script names no machine path: everything the build passes in is an ISCC define.

#ifndef AppVersion
  #error "Pass /DAppVersion=<version> (INS-1)."
#endif
#ifndef SourceDir
  #error "Pass /DSourceDir=<the staged tree> (INS-1)."
#endif

; INS-6: the AppId is made once and never changed. A /DTest build has its own, so it never upgrades or removes a real install.
#ifdef Test
  #define AppIdRaw "{7E1B5C0A-2D44-4B6F-9A3E-5C1D8F20A6B1}"
#else
  #define AppIdRaw "{49C88D34-DF04-438F-9277-D89DBE43A98B}"
#endif

#define TrayRel "out\windows\scheduler\scheduler-tray.exe"
#define TrayArgs "-Pool"
#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppIdRaw + "_is1"

[Setup]
; Inno reads "{{" as a literal "{".
AppId={#StringChange(AppIdRaw, "{", "{{")}
AppName=ProjectToolkit
AppVersion={#AppVersion}
AppPublisher=Trevor W. Walker
; INS-5: per user by default, all users allowed from the dialog or the command line.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\ProjectToolkit
DisableDirPage=no
DisableProgramGroupPage=yes
; INS-19: same folder and same task choices as the old install; no Restart Manager.
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\{#TrayRel}
LicenseFile={#SourceDir}\LICENSE
; INS-1: one file with a fixed name.
OutputBaseFilename=ProjectToolkit-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; INS-9: the PATH task changes HKCU\Environment.
ChangesEnvironment=yes
WizardStyle=modern
#ifdef Sign
; INS-23: signing hook, off by default. Name the tool with ISCC's /Ssigntool=<command> and add /DSign.
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; INS-8 and INS-9: both offered, both unticked.
Name: "startatsignin"; Description: "Start Switchyard when I sign in"; Flags: unchecked
Name: "addtopath"; Description: "Add the bin folder to my PATH (so projecttoolkit.ps1 can be run by name)"; Flags: unchecked

[Files]
; INS-3: the staged tree (the release tree less code\ and test\, plus the published binaries).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; INS-20: files a new version dropped must not linger.
Type: filesandordirs; Name: "{app}\out\windows\scheduler"
Type: filesandordirs; Name: "{app}\assets\templates"

[Icons]
; INS-7: Start menu only, no desktop icon.
Name: "{autoprograms}\Switchyard"; Filename: "{app}\{#TrayRel}"; Parameters: "{#TrayArgs}"; WorkingDir: "{app}"; IconFilename: "{app}\{#TrayRel}"

[Registry]
; INS-8: start at sign-in, same command as the shortcut.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Switchyard"; \
  ValueData: """{app}\{#TrayRel}"" {#TrayArgs}"; Flags: uninsdeletevalue; Tasks: startatsignin
; INS-9: add {app}\bin to the user PATH; the uninstall code below removes exactly that entry.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\bin"; \
  Check: NeedsPathEntry; Tasks: addtopath

[Run]
; INS-13: offered only when the runtime is there and a pool file exists.
Filename: "{app}\{#TrayRel}"; Parameters: "{#TrayArgs}"; WorkingDir: "{app}"; \
  Description: "Start Switchyard"; Flags: nowait postinstall skipifsilent; Check: OfferStart
; INS-21: after an upgrade the new tray (and its scheduler, which adopts the workers) starts again, silent runs too.
Filename: "{app}\{#TrayRel}"; Parameters: "{#TrayArgs}"; WorkingDir: "{app}"; Flags: nowait; Check: RestartTray

[Code]
const
  RuntimeUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';
  GitUrl = 'https://git-scm.com/download/win';
  UninstKey = '{#UninstallKey}';

var
  TrayWasRunning: Boolean;
  MissingPage: TOutputMsgWizardPage;

// ---- the runtime and git (INS-12) ----

function DesktopRuntimeFound: Boolean;
var
  Root, Dir: String;
  Found: TFindRec;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
    Root := ExpandConstant('{commonpf64}\dotnet');
  Dir := AddBackslash(Root) + 'shared\Microsoft.WindowsDesktop.App\';
  if FindFirst(Dir + '10.*', Found) then
  try
    repeat
      if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

function GitOnPath: Boolean;
begin
  Result := FileSearch('git.exe', GetEnv('PATH')) <> '';
end;

function RestartTray: Boolean; forward;

function OfferStart: Boolean;
begin
  Result := not RestartTray and DesktopRuntimeFound and FileExists(ExpandConstant('{localappdata}\ProjectToolkit\pool.json'));
end;

procedure OpenUrl(const Url: String);
var
  Code: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure RuntimeButtonClick(Sender: TObject);
begin
  OpenUrl(RuntimeUrl);
end;

procedure GitButtonClick(Sender: TObject);
begin
  OpenUrl(GitUrl);
end;

procedure InitializeWizard;
var
  Text: String;
  Button: TNewButton;
  Top: Integer;
begin
  // A page after the licence, shown only when something is missing. It never stops the installation.
  MissingPage := CreateOutputMsgPage(wpLicense, 'Before you start Switchyard', 'Things it needs on this PC', '');
  Text := '';
  if not DesktopRuntimeFound then
    Text := Text + 'The .NET 10 Desktop Runtime (x64) was not found. Switchyard needs it to run.' + #13#10#13#10;
  if not GitOnPath then
    Text := Text + 'Git was not found on PATH. The toolkit uses it for your projects.' + #13#10#13#10;
  Text := Text + 'The installation goes on either way; install them whenever you like.';
  MissingPage.MsgLabel.Caption := Text;
  Top := MissingPage.MsgLabel.Top + MissingPage.MsgLabel.Height + ScaleY(12);
  if not DesktopRuntimeFound then
  begin
    Button := TNewButton.Create(MissingPage);
    Button.Parent := MissingPage.Surface;
    Button.Caption := 'Open the Desktop Runtime download (Windows x64)';
    Button.Width := ScaleX(300);
    Button.Top := Top;
    Button.OnClick := @RuntimeButtonClick;
    Top := Top + Button.Height + ScaleY(8);
  end;
  if not GitOnPath then
  begin
    Button := TNewButton.Create(MissingPage);
    Button.Parent := MissingPage.Surface;
    Button.Caption := 'Open the Git for Windows download';
    Button.Width := ScaleX(300);
    Button.Top := Top;
    Button.OnClick := @GitButtonClick;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (MissingPage <> nil) and (PageID = MissingPage.ID) and DesktopRuntimeFound and GitOnPath;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and not OfferStart then
  begin
    if not DesktopRuntimeFound then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'Install the .NET 10 Desktop Runtime before you start Switchyard.'
    else
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'There is no pool file yet. The next step is the guide''s "create a project" ' +
        '(' + ExpandConstant('{app}\bin\projecttoolkit.ps1') + ' -Action new-project), which creates it.';
  end;
end;

// ---- stopping a running copy (INS-19, INS-22) ----

function OldInstallDir: String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstKey, 'InstallLocation', Result) then
    if not RegQueryStringValue(HKLM, UninstKey, 'InstallLocation', Result) then
      Result := '';
end;

// Runs <exe> -StopForUpdate. Returns its exit code (0: nothing of the kit is running any more); Line is its one line.
function RunStopForUpdate(const Exe: String; var Line: String): Integer;
var
  OutFile: String;
  Lines: TArrayOfString;
begin
  OutFile := ExpandConstant('{tmp}\stopforupdate.txt');
  DeleteFile(OutFile);
  Line := 'The running Switchyard could not be stopped.';
  if not Exec(ExpandConstant('{cmd}'), '/C ""' + Exe + '" -StopForUpdate > "' + OutFile + '" 2>&1"', '', SW_HIDE,
       ewWaitUntilTerminated, Result) then
  begin
    Result := 1;
    Log('stop for update: ' + Exe + ' exit code ' + IntToStr(Result) + ': ' + Line);
    Exit;
  end;
  if LoadStringsFromFile(OutFile, Lines) and (GetArrayLength(Lines) > 0) and (Trim(Lines[0]) <> '') then
    Line := Trim(Lines[0]);
  Log('stop for update: ' + Exe + ' exit code ' + IntToStr(Result) + ': ' + Line);
end;

// INS-19: PrepareToInstall stops the old copy before anything is copied. Retry or Cancel when it cannot.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, Exe, Line: String;
  Code: Integer;
begin
  Result := '';
  // 1258-B: INS-21's "a tray was running" is this copy's tray, as the stop's own line says: a tray of another
  // folder (a development checkout's) is left alone and is no reason to start this one.
  TrayWasRunning := False;
  Dir := OldInstallDir;
  if Dir = '' then
    Exit;
  Exe := AddBackslash(Dir) + '{#TrayRel}';
  if not FileExists(Exe) then
    Exit;
  repeat
    Code := RunStopForUpdate(Exe, Line);
    if (Pos('the tray was asked to exit', Line) > 0) or (Pos('still running; close', Line) > 0) then
      TrayWasRunning := True;
    if (Code <> 0) and (MsgBox(Line + #13#10#13#10 + 'Retry to try again, or Cancel to stop the installation.',
         mbError, MB_RETRYCANCEL) = IDCANCEL) then
    begin
      Result := Line;
      Exit;
    end;
  until Code = 0;
end;

// INS-21: an older installer over a newer install asks before going on.
function InitializeSetup: Boolean;
var
  Old: String;
  OldV, MineV: Int64;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, UninstKey, 'DisplayVersion', Old) then
    if not RegQueryStringValue(HKLM, UninstKey, 'DisplayVersion', Old) then
      Exit;
  if StrToVersion(Old, OldV) and StrToVersion('{#AppVersion}', MineV) and (ComparePackedVersion(OldV, MineV) > 0) then
    Result := SuppressibleMsgBox('ProjectToolkit ' + Old + ' is already installed and is newer than this installer ({#AppVersion}). ' +
      'Install the older version over it?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

function RestartTray: Boolean;
begin
  // INS-21: a tray was running before the upgrade, or start at sign-in is ticked.
  Result := (TrayWasRunning or WizardIsTaskSelected('startatsignin')) and DesktopRuntimeFound;
end;

// ---- PATH entry (INS-9) ----

function NeedsPathEntry: Boolean;
var
  Paths: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Paths) then
    Paths := '';
  Result := Pos(';' + Uppercase(ExpandConstant('{app}\bin')) + ';', ';' + Uppercase(Paths) + ';') = 0;
end;

procedure RemovePathEntry;
var
  Paths, Entry: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Paths) then
    Exit;
  Entry := ExpandConstant('{app}\bin');
  Paths := ';' + Paths + ';';
  P := Pos(';' + Uppercase(Entry) + ';', Uppercase(Paths));
  if P = 0 then
    Exit;
  Delete(Paths, P, Length(Entry) + 1);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Paths);
end;

// ---- uninstall (INS-22) ----

function InitializeUninstall: Boolean;
var
  Exe, Line: String;
begin
  Result := True;
  Exe := ExpandConstant('{app}\{#TrayRel}');
  if FileExists(Exe) and (RunStopForUpdate(Exe, Line) <> 0) then
    Result := MsgBox(Line + #13#10#13#10 + 'Remove ProjectToolkit anyway?', mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

// 1265-B, INS-22: this kit folder's tray copy goes, and no other kit's. The installed tray removes its own, because
// it alone knows the copy's per-kit folder; a non-zero exit (a file of it in use) is not an error for the uninstall.
procedure RemoveTrayCopy;
var
  Exe: String;
  Code: Integer;
begin
  Exe := ExpandConstant('{app}\{#TrayRel}');
  if FileExists(Exe) then
    Exec(Exe, '-RemoveTrayCopy', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveTrayCopy;
    RemovePathEntry;
  end;
end;
