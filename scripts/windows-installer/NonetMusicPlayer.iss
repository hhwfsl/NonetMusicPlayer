#ifndef AppVersion
  #error AppVersion is required. Build through scripts/build-windows-installer.ps1.
#endif
#ifndef RepositoryRoot
  #define RepositoryRoot "..\.."
#endif
#ifndef PackageDirectory
  #define PackageDirectory RepositoryRoot + "\publish\desktop\windows"
#endif
#ifndef AppArchitecture
  #define AppArchitecture "x64"
#endif
#if AppArchitecture == "x64"
  #define AllowedArchitectures "x64compatible"
  #define InstallDirectory "NonetMusicPlayer"
  #define InstallationSuffix ""
#elif AppArchitecture == "arm64"
  #define AllowedArchitectures "arm64"
  #define InstallDirectory "NonetMusicPlayer-arm64"
  #define InstallationSuffix ".arm64"
#elif AppArchitecture == "x86"
  #define AllowedArchitectures "x86compatible"
  #define InstallDirectory "NonetMusicPlayer-x86"
  #define InstallationSuffix ".x86"
#else
  #error Unsupported AppArchitecture
#endif

[Setup]
AppId={code:InstallationId}
AppName=Nonet
AppVersion={#AppVersion}
AppPublisher=NonetMusicPlayer
VersionInfoVersion={#AppFileVersion}
DefaultDirName={userpf}\{#InstallDirectory}
DefaultGroupName=Nonet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitectures}
#if AppArchitecture != "x86"
ArchitecturesInstallIn64BitMode={#AllowedArchitectures}
#endif
MinVersion=10.0.19045
OutputDir={#RepositoryRoot}\publish\desktop\windows_installer
OutputBaseFilename=NonetMusicPlayer-{#AppVersion}-windows-{#AppArchitecture}-setup
SetupIconFile={#RepositoryRoot}\src\NonetMusicPlayer.Desktop\Assets\icon.ico
UninstallDisplayIcon={app}\Nonet.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=Nonet.exe
RestartApplications=no
Uninstallable=yes
UninstallFilesDir={app}\Uninstall
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousLanguage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,{#RepositoryRoot}\scripts\windows-installer\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PackageDirectory}\Nonet.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\NonetCrashHandler.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\e_sqlite3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\libHarfBuzzSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\libSkiaSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\miniaudio.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\soundflow-ffmpeg.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\TagLibSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\UserManual.zh-CN.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\UserManual.en-US.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\UserManual.ja-JP.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\VideoDecoder\ffmpeg.exe"
Type: files; Name: "{app}\VideoDecoder\ffmpeg"
Type: files; Name: "{app}\VideoDecoder\LICENSE.txt"
Type: files; Name: "{app}\VideoDecoder\NOTICE.txt"
Type: dirifempty; Name: "{app}\VideoDecoder"

[Icons]
Name: "{group}\Nonet"; Filename: "{app}\Nonet.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Nonet"; Filename: "{app}\Nonet.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Nonet.exe"; Description: "{cm:LaunchProgram,Nonet}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InstallationId(Param: String): String;
begin
  if ExpandConstant('{param:LMPQA|0}') = '1' then
    Result := 'NonetMusicPlayer.InstallerQA{#InstallationSuffix}'
  else
    Result := 'NonetMusicPlayer.Desktop{#InstallationSuffix}';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Probe: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if not ForceDirectories(ExpandConstant('{app}')) then
    begin
      MsgBox('The installation folder must be writable by the current user.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Probe := ExpandConstant('{app}\nonet-write-test.tmp');
    if not SaveStringToFile(Probe, 'NonetMusicPlayer', False) then
    begin
      MsgBox('Choose a writable folder. Player data is stored next to the application.', mbError, MB_OK);
      Result := False;
    end
    else DeleteFile(Probe);
  end;
end;

// No broad uninstall deletion: personal Data and bootstrap files are retained.
