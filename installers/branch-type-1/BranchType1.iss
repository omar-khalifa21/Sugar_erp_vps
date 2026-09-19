#ifndef PublishDir
  #error PublishDir must point to the self-contained Branch Type 1 publish directory.
#endif
#ifndef OutputDir
  #define OutputDir ".\artifacts"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef Variant
  #define Variant "Desktop"
#endif

[Setup]
#if Variant == "Touch"
AppId={{FA162747-6CC0-4C41-90C8-5A966D6A7A6E}
#else
AppId={{9E909276-7AD3-447B-B64F-1BE16A52C8BD}
#endif
AppName=Sugar ERP - Branch1 {#Variant}
AppVersion={#AppVersion}
AppPublisher=Sugar
DefaultDirName={autopf}\Sugar ERP\Branch1 {#Variant}
DefaultGroupName=Sugar ERP
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Branch1-{#Variant}-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\..\apps\branch-type-1\Assets\sugar.ico
UninstallDisplayIcon={app}\SugarERP.Branch1.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#if Variant == "Touch"
Source: "touch.variant"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\Sugar ERP\Branch1 {#Variant}"; Filename: "{app}\SugarERP.Branch1.exe"
Name: "{autodesktop}\Sugar ERP - Branch1 {#Variant}"; Filename: "{app}\SugarERP.Branch1.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات إضافية:"

[Run]
Filename: "{app}\SugarERP.Branch1.exe"; Description: "تشغيل Sugar ERP - Branch Type 1"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
