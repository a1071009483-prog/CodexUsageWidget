; Codex Usage Widget per-user installer.
; Installs under %LOCALAPPDATA%\Programs\CodexUsageWidget without elevation.
; Persistent application state under %LOCALAPPDATA%\CodexUsageWidget is never
; touched by install or uninstall. Windows startup registration is owned by the
; in-app "Start with Windows" preference, not by this installer.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #error SourceDir must be defined (pass /DSourceDir=<publish directory>)
#endif

[Setup]
AppId={{C5A6B234-4E3E-4C2F-9E2B-7B43DA9B7D7A}
AppName=Codex Usage Widget
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\CodexUsageWidget
DefaultGroupName=Codex Usage Widget
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
UninstallDisplayName=Codex Usage Widget
OutputBaseFilename=CodexUsageWidget-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Codex Usage Widget"; Filename: "{app}\CodexUsageWidget.exe"

[Run]
Filename: "{app}\CodexUsageWidget.exe"; Description: "Launch Codex Usage Widget"; Flags: nowait postinstall skipifsilent
