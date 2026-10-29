; Gridify installer (Inno Setup 6). Build: ISCC.exe /DAppVersion=1.0.0 installer\Gridify.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\build"
#endif

[Setup]
AppId={{009F5B3C-29F9-4BCF-AB1D-E050F20C3BD9}
AppName=Gridify
AppVersion={#AppVersion}
AppVerName=Gridify {#AppVersion}
AppPublisher=Pignition Studio
DefaultDirName={autopf}\Gridify
DefaultGroupName=Gridify
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=Gridify-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\assets\Gridify.ico
UninstallDisplayIcon={app}\Gridify.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#ifexist "ChineseTraditional.isl"
Name: "chinesetraditional"; MessagesFile: "ChineseTraditional.isl"
#endif

[Tasks]
Name: "startup"; Description: "Start Gridify when I sign in to Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Gridify.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Gridify"; Filename: "{app}\Gridify.exe"

[Registry]
; Optional start-with-Windows entry (same value the app itself writes). Removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Gridify"; ValueData: """{app}\Gridify.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Gridify"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Gridify.exe"; Description: "Launch Gridify"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Always give the desktop icons back to Windows before files are removed.
Filename: "{app}\Gridify.exe"; Parameters: "--restore"; Flags: runhidden waituntilterminated; RunOnceId: "GridifyRestore"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im Gridify.exe"; Flags: runhidden waituntilterminated; RunOnceId: "GridifyKill"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\Gridify');
    if DirExists(DataDir) and (not UninstallSilent()) then
      if MsgBox('Also delete your Gridify settings and backups?' + #13#10 + DataDir + #13#10#13#10 +
                'Your original files are never touched.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
