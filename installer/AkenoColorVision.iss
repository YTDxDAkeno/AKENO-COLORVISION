; Inno Setup script for Akeno ColorVision (per-user install, no administrator rights).
; Build: iscc /DAppVersion=0.1.0 /DSourceDir=..\dist\Akeno-ColorVision installer\AkenoColorVision.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\Akeno-ColorVision"
#endif

[Setup]
AppId={{6A1F0C52-8D1E-4B7A-9C35-0D6F7E1A2B00}
AppName=Akeno ColorVision
AppVersion={#AppVersion}
AppVerName=Akeno ColorVision {#AppVersion}
AppPublisher=Akeno ColorVision contributors
AppPublisherURL=https://github.com/ytdxdakeno/akeno-colorvision
DefaultDirName={localappdata}\Programs\Akeno ColorVision
DefaultGroupName=Akeno ColorVision
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Akeno-ColorVision-Setup-v{#AppVersion}
SetupIconFile=..\assets\akeno.ico
UninstallDisplayIcon={app}\AkenoColorVision.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "reseticon"; Description: "Create a desktop shortcut for Emergency Reset"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\AkenoColorVision.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\AkenoColorVision-EmergencyReset.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\READ ME FIRST.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#SourceDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Akeno ColorVision"; Filename: "{app}\AkenoColorVision.exe"
Name: "{group}\Akeno ColorVision - Emergency Reset"; Filename: "{app}\AkenoColorVision-EmergencyReset.exe"; Comment: "Restore normal screen colors"
Name: "{group}\Akeno ColorVision (Safe mode)"; Filename: "{app}\AkenoColorVision.exe"; Parameters: "--safe-mode"
Name: "{autodesktop}\Akeno ColorVision"; Filename: "{app}\AkenoColorVision.exe"; Tasks: desktopicon
Name: "{autodesktop}\Akeno ColorVision - Emergency Reset"; Filename: "{app}\AkenoColorVision-EmergencyReset.exe"; Tasks: reseticon

[Registry]
; The app creates this value only if the user enables "Start with Windows"; remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "AkenoColorVision"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\AkenoColorVision.exe"; Description: "Start Akeno ColorVision now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the screen colors are normal and the app is closed before files are removed.
Filename: "{app}\AkenoColorVision-EmergencyReset.exe"; Parameters: "--quiet"; Flags: runhidden waituntilterminated; RunOnceId: "ResetColors"
