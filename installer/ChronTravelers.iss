; ChronTravelers Windows installer script (Inno Setup 6).
; docs/AGENTS.md's Packaging/Release Agent owns this file: "Must not
; change game code to 'make packaging easier' — packaging adapts to the
; app, not the reverse."
;
; This script packages the self-contained single-file build produced by:
;   dotnet publish src\ChronTravelers.Console -c Release -r win-x64 -o publish\win-x64
; (see src\ChronTravelers.Console\ChronTravelers.Console.csproj for the publish
; properties — SelfContained/PublishSingleFile/etc.)
;
; Build locally (from the repo root, with Inno Setup 6 installed):
;   dotnet publish src\ChronTravelers.Console -c Release -r win-x64 -o publish\win-x64
;   iscc installer\ChronTravelers.iss
; The finished installer lands in installer\Output\.
;
; MyAppVersion can be overridden from the command line (the release CI
; workflow does this from the pushed git tag):
;   iscc /DMyAppVersion=1.2.3 installer\ChronTravelers.iss

#define MyAppName "ChronTravelers"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "ChronTravelers Project"
#define MyAppURL "https://github.com/jdickinson9691/Mutants"
#define MyAppExeName "ChronTravelers.exe"
#define MyPublishDir "..\publish\win-x64"

[Setup]
; A fixed AppId (distinct from the app name) so Windows recognizes
; upgrades/uninstalls across versions correctly - do not change this once
; a version has shipped.
AppId={{730BDF16-DA97-4DF6-8574-1F5F730CCDE1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install with no admin prompt - appropriate for a single-player
; game with a per-user save file (see ChronTravelers.Console's %APPDATA% save
; path), and keeps the installer usable without elevation.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=ChronTravelersSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The game is a text/console app by design (docs/GDD.md §10) - no custom
; wizard graphics needed.
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; The tier-free content catalogs (eras, species, item archetypes, ...) can't
; be bundled into the single-file exe, so `dotnet publish` drops them next
; to it under Content\ (see ChronTravelers.Console.csproj). ChronTravelers.Engine's
; ContentLoader loads them from AppContext.BaseDirectory\Content at startup;
; without them the game silently falls back to a tiny 3-era sandbox.
Source: "{#MyPublishDir}\Content\*"; DestDir: "{app}\Content"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; The game is a console app: launching it after install opens a console
; window, which is the intended play experience.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
