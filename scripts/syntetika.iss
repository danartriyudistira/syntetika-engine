; SPDX-License-Identifier: AGPL-3.0-only
; Inno Setup script for the Syntetika Engine Windows installer. Driven by
; scripts/package-win-syntetika.ps1, which passes /DAppVersion, /DArch and /DPubDir.
; Kept separate from nota.iss so the fork's installer can live beside a stock Nota.
;
; Installs per user by default (no UAC) into %LOCALAPPDATA%\Programs\Syntetika Engine;
; an administrator can choose an all-users install on the privileges page.

#define AppName "Syntetika Engine"
#define AppExe "Nota.App.exe"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PubDir
  #define PubDir "..\dist\publish-" + Arch
#endif

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Syntetika
AppPublisherURL=https://github.com/danartriyudistira/syntetika-engine
AppSupportURL=https://github.com/danartriyudistira/syntetika-engine/issues
AppId={{7B2E9A54-3C61-4E8F-9D32-5A0C81F4E6D7}
DefaultDirName={autopf}\Syntetika Engine
DefaultGroupName=Syntetika Engine
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=Syntetika-Engine-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\nota\assets\icons\windows\syntetika.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-user by default: the app needs no administrator rights. Choosing "install for all
; users" on the privileges page elevates and uses Program Files instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
InfoBeforeFile={#PubDir}\licenses\SOURCE.txt
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"
Name: "associate"; Description: "Open .syntetika projects with {#AppName}"; GroupDescription: "Explorer:"
Name: "folderopen"; Description: "Add ""Open with {#AppName}"" to legacy .nota folders"; GroupDescription: "Explorer:"

[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Syntetika Engine"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Syntetika Engine"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; A project is one file (`.syntetika`, 0.1.6) or a legacy `.nota` folder. The file gets a real
; association; the folder gets a context entry, because Windows always opens a folder in
; Explorer whatever its extension is. HKA follows the install mode: HKCU or HKLM.
Root: HKA; Subkey: "Software\Classes\.syntetika"; ValueType: string; ValueName: ""; ValueData: "SyntetikaEngine.Project"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\SyntetikaEngine.Project"; ValueType: string; ValueName: ""; ValueData: "Syntetika Engine Project"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\SyntetikaEngine.Project"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Syntetika Engine Project"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\SyntetikaEngine.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\SyntetikaEngine.Project\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\SyntetikaEngine.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.syntetika\OpenWithProgids"; ValueType: string; ValueName: "SyntetikaEngine.Project"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate

Root: HKA; Subkey: "Software\Classes\Directory\shell\SyntetikaEngine"; ValueType: string; ValueName: ""; ValueData: "Open with {#AppName}"; Flags: uninsdeletekey; Tasks: folderopen
Root: HKA; Subkey: "Software\Classes\Directory\shell\SyntetikaEngine"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileName:~=""*.nota"""; Tasks: folderopen
Root: HKA; Subkey: "Software\Classes\Directory\shell\SyntetikaEngine"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe},0"; Tasks: folderopen
Root: HKA; Subkey: "Software\Classes\Directory\shell\SyntetikaEngine\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: folderopen

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Syntetika Engine"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leftovers of the in-app updater/telemetry-free logs are not deleted: user projects and
; settings live in %APPDATA%\Syntetika Engine, which an uninstall keeps.
