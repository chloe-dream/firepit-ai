; Firepit installer — Inno Setup script
;
; Build: ISCC.exe installer\firepit.iss
;        (Defaults to current source-tree version. Override per build via
;         /DAppVersion=1.12.0 on the ISCC command line — used by CI.)
;
; Inputs (must exist before ISCC runs):
;   - publish/win-x64/Firepit.exe   (from `dotnet publish` single-file)
;   - src/Firepit/firepit.ico
;
; Output:
;   - installer/output/FirepitSetup-<version>-win-x64.exe

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{8E0E9F4C-5C9A-4A3E-9F5A-FIREPITAPPID01}}
AppName=Firepit
AppVersion={#AppVersion}
AppPublisher=Chloe Bernette
AppPublisherURL=https://github.com/chloe-dream/firepit-ai
AppSupportURL=https://github.com/chloe-dream/firepit-ai/issues
AppUpdatesURL=https://github.com/chloe-dream/firepit-ai/releases
DefaultDirName={localappdata}\Programs\Firepit
DefaultGroupName=Firepit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=FirepitSetup-{#AppVersion}-win-x64
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Firepit\firepit.ico
UninstallDisplayIcon={app}\Firepit.exe
UninstallDisplayName=Firepit {#AppVersion}
WizardImageStretch=no
ShowLanguageDialog=no
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\Firepit.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Firepit"; Filename: "{app}\Firepit.exe"
Name: "{group}\Uninstall Firepit"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Firepit"; Filename: "{app}\Firepit.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Firepit.exe"; Description: "Launch Firepit"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave user data in %APPDATA%\Firepit and %LOCALAPPDATA%\Firepit alone by
; default — projects and settings survive an uninstall.

[Code]
var
  ProjectsPage: TInputDirWizardPage;

procedure InitializeWizard;
begin
  ProjectsPage := CreateInputDirPage(
    wpSelectDir,
    'Where do your projects live?',
    'Firepit scans this folder for projects with a CLAUDE.md or .claude/ marker.',
    'Pick a folder. You can change this later in Firepit''s settings.',
    False, '');
  ProjectsPage.Add('Projects root:');
  ProjectsPage.Values[0] := ExpandConstant('{userdocs}');
end;

function GetProjectsRoot(Param: string): string;
begin
  Result := ProjectsPage.Values[0];
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDataDir: string;
  MarkerPath: string;
  SettingsPath: string;
  Root: AnsiString;
begin
  if CurStep = ssPostInstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\Firepit');
    SettingsPath := AppDataDir + '\settings.json';
    MarkerPath := AppDataDir + '\first-run-projects-root.txt';

    if not DirExists(AppDataDir) then
      ForceDirectories(AppDataDir);

    // Only write the marker on a clean install (no settings.json yet). On
    // upgrade-over-install the user already has a configured projects root
    // and we must not stomp it.
    if not FileExists(SettingsPath) then
    begin
      Root := AnsiString(GetProjectsRoot(''));
      SaveStringToFile(MarkerPath, Root, False);
    end;
  end;
end;
