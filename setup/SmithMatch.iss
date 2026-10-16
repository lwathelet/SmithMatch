; Inno Setup script for SmithMatch
; Build first:  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
; Then:         "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup\SmithMatch.iss

#define AppName "SmithMatch"
#define AppVersion "1.2.0"
#define AppPublisher "Wattlet AS"
#define AppURL "https://github.com/lwathelet/SmithMatch"
#define AppExe "SmithMatch.exe"

[Setup]
AppId={{6E3B1F2A-8C4D-4E5F-9A7B-2D1C0E9F8A61}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\assets\wattlet.ico
WizardSmallImageFile=..\assets\wizard-small.bmp
LicenseFile=..\LICENSE
OutputDir=.
OutputBaseFilename=SmithMatch-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assoc"; Description: "Open .smith design files with {#AppName}"; GroupDescription: "File association:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\demo.smith"; DestDir: "{app}\Examples"; Flags: ignoreversion
Source: "..\docs\parasitics-demo.smith"; DestDir: "{app}\Examples"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Demo design"; Filename: "{app}\{#AppExe}"; Parameters: """{app}\Examples\demo.smith"""
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.smith"; ValueType: string; ValueName: ""; ValueData: "SmithMatch.Design"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\SmithMatch.Design"; ValueType: string; ValueName: ""; ValueData: "SmithMatch design"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\SmithMatch.Design\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\SmithMatch.Design\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assoc

[Run]
Filename: "{app}\{#AppExe}"; Parameters: """{app}\Examples\demo.smith"""; Description: "{cm:LaunchProgram,{#AppName}} (demo design)"; Flags: nowait postinstall skipifsilent
