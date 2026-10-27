; UniFetcher installer (Inno Setup). Based on Parabolic's installer by Nickvision.
; The release workflow passes the version and output name, e.g.:
;   iscc /DMyAppVersion=1.0.0 /DMyArch=x64 setup.iss

#ifndef MyAppVersion
  #define MyAppVersion       "0.1.0"
#endif
#ifndef MyNumericVersion
  #define MyNumericVersion   "0.1.0"
#endif
#ifndef MyArch
  #define MyArch             "x64"
#endif
#define MyAppName            "UniFetcher"
#define MyAppShortName       "UniFetcher"
#define MyAppShortNameLower  "unifetcher"
#define MyAppPublisher       "UniFetcher"
#define MyAppURL             "https://github.com/Obamna1234/UniFetcher-Windows"
#define MyAppExeName         "UniFetcher.exe"
#define StartYearCopyright   "2021"
#define CurrentYear          GetDateTimeString('yyyy','','')
[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{2CDEDF59-E562-4471-81EB-CC35793DFA91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}

AppCopyright=Parabolic {#StartYearCopyright}-{#CurrentYear} Nickvision; UniFetcher changes {#CurrentYear} Obamna1234
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases

VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoVersion={#MyNumericVersion}

UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\Release\{#MyAppExeName}
AppPublisher={#MyAppPublisher}

ShowLanguageDialog=yes
UsePreviousLanguage=no
LanguageDetectionMethod=uilanguage

#if MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

WizardStyle=modern dynamic windows11
WizardResizable=yes
WizardSizePercent=120,130

DefaultDirName={autopf}\{#MyAppName}
LicenseFile=..\License.rtf
OutputDir=..\inno
OutputBaseFilename=UniFetcher-{#MyAppVersion}-Setup-{#MyArch}
SetupIconFile=..\resources\unifetcher.ico
Compression=lzma
SolidCompression=yes
; UniFetcher installs just for you (in %LOCALAPPDATA%\Programs), so no administrator prompt is needed
PrivilegesRequired=lowest
CloseApplications=yes
RestartIfNeededByRun=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "slovak"; MessagesFile: "compiler:Languages\Slovak.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "tamil"; MessagesFile: "compiler:Languages\Tamil.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "yt-dlp.exe"; DestDir: "{app}\Release"; Flags: ignoreversion
Source: "ffmpeg.exe"; DestDir: "{app}\Release"; Flags: ignoreversion
Source: "ffprobe.exe"; DestDir: "{app}\Release"; Flags: ignoreversion
Source: "deno.exe"; DestDir: "{app}\Release"; Flags: ignoreversion
Source: "..\{#GetEnv('APP_FILES_PATH')}\{#MyAppExeName}"; DestDir: "{app}\Release"; Flags: ignoreversion
Source: "..\{#GetEnv('APP_FILES_PATH')}\*"; DestDir: "{app}\Release"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppShortName}"; Filename: "{app}\Release\{#MyAppExeName}"; Tasks: quicklaunchicon
Name: "{autodesktop}\{#MyAppShortName}"; Filename: "{app}\Release\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; (none: UniFetcher no longer registers a unifetcher:// link handler)

[Run]
Filename: "{app}\Release\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked
