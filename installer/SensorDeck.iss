; SensorDeck installer (Inno Setup 6). Built by build\release.ps1, which passes AppVersion and SourceDir.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

[Setup]
AppId={{6F1C2B7E-4E0B-4C59-9D1B-5D8E3A0C7F41}
AppName=SensorDeck
AppVersion={#AppVersion}
AppVerName=SensorDeck {#AppVersion}
AppPublisher=SensorDeck contributors
AppComments=Free, lightweight hardware monitor for Windows
DefaultDirName={autopf}\SensorDeck
DefaultGroupName=SensorDeck
DisableProgramGroupPage=yes
; The app itself needs administrator rights for sensor access, so install per-machine.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\SensorDeck.App\Assets\SensorDeck.ico
UninstallDisplayIcon={app}\SensorDeck.exe
UninstallDisplayName=SensorDeck
OutputDir=..\dist
OutputBaseFilename=SensorDeck-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
; Close a running copy before upgrading/uninstalling.
CloseApplications=yes
AppMutex=Local\SensorDeck.SingleInstance
VersionInfoVersion={#AppVersion}
VersionInfoDescription=SensorDeck installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\SensorDeck.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SensorDeck"; Filename: "{app}\SensorDeck.exe"; Comment: "Hardware monitor"
Name: "{autodesktop}\SensorDeck"; Filename: "{app}\SensorDeck.exe"; Tasks: desktopicon

[Run]
; postinstall entries run as the original NON-elevated user by default, and SensorDeck's manifest requires
; administrator, so CreateProcess fails with error 740. runascurrentuser launches it with Setup's own admin
; rights instead (no second UAC prompt).
Filename: "{app}\SensorDeck.exe"; Description: "Launch SensorDeck"; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "https://pawnio.eu/"; Description: "Get the free PawnIO driver (enables CPU temperature, clock speed and motherboard fan readings)"; \
    Flags: shellexec postinstall skipifsilent unchecked nowait; Check: not PawnIOInstalled

[UninstallRun]
; Remove the "Start with Windows" task if the user enabled it.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""SensorDeck"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
function PawnIOInstalled: Boolean;
begin
  { PawnIO registers an uninstall entry; checking it avoids suggesting the driver twice. }
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;
