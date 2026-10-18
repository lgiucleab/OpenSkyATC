; OpenSkyATC installer (step 17), built with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
; Build with: python installer/build.py   (publishes the app, assembles the MSFS packages, runs ISCC)
; Writes artifacts/installer/OpenSkyATC-Setup-<version>.exe. Installing also creates the uninstaller
; (unins000.exe, "Uninstall OpenSkyATC" in the Start menu and in Windows Settings > Apps).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "OpenSkyATC"
#define Root ".."

[Setup]
AppId={{6F1C2B7E-4E0A-4C55-9B7E-2C9D1A0F5E31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=OpenSkyATC contributors
AppPublisherURL=https://github.com/lgiucleab/OpenSkyATC
AppSupportURL=https://github.com/lgiucleab/OpenSkyATC/issues
AppUpdatesURL=https://github.com/lgiucleab/OpenSkyATC/releases
; Per-user install: no administrator rights, and the sim's Community folder is in the user's profile too.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile={#Root}\LICENSE
OutputDir={#Root}\artifacts\installer
OutputBaseFilename=OpenSkyATC-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\OpenSkyATC.exe
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.PackagesTask=Install the MSFS 2024 toolbar panel and taxi arrows (Community folder)
spanish.PackagesTask=Instalar el panel de la barra de herramientas y las flechas de rodaje de MSFS 2024 (carpeta Community)
english.CommunityTitle=MSFS 2024 Community folder
spanish.CommunityTitle=Carpeta Community de MSFS 2024
english.CommunityDesc=Where should the OpenSkyATC packages go?
spanish.CommunityDesc=¿Dónde se instalan los paquetes de OpenSkyATC?
english.CommunityPrompt=The sim's Community folder (found from its settings; change it if you moved it):
spanish.CommunityPrompt=La carpeta Community del simulador (encontrada en su configuración; cámbiala si la moviste):
english.NoSdk=OpenSkyATC connects to MSFS 2024 through SimConnect, which comes with the free Microsoft Flight Simulator 2024 SDK. It was not found on this PC.%n%nInstall the SDK (in the sim: Options > General > Developers > Developer Mode, then Help > SDK Installer), then start OpenSkyATC. You can install OpenSkyATC now and the SDK later.
spanish.NoSdk=OpenSkyATC se conecta con MSFS 2024 mediante SimConnect, que viene con el SDK gratuito de Microsoft Flight Simulator 2024. No se encontró en este PC.%n%nInstala el SDK (en el simulador: Opciones > General > Desarrolladores > Modo desarrollador, luego Ayuda > Instalador del SDK) y después inicia OpenSkyATC. Puedes instalar OpenSkyATC ahora y el SDK más tarde.
english.SimRunning=MSFS 2024 is running: the panel and arrows appear after you restart the sim.
spanish.SimRunning=MSFS 2024 está en marcha: el panel y las flechas aparecerán al reiniciar el simulador.
english.DeleteData=Also delete OpenSkyATC's downloaded voices and speech models, its settings and your flight debriefs?
spanish.DeleteData=¿Borrar también las voces y modelos de voz descargados, la configuración y los resúmenes de vuelo de OpenSkyATC?
english.UninstallShortcut=Uninstall OpenSkyATC
spanish.UninstallShortcut=Desinstalar OpenSkyATC

[Tasks]
Name: "packages"; Description: "{cm:PackagesTask}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The app, .NET included (self-contained); only the x64 natives are needed.
Source: "{#Root}\artifacts\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb,\runtimes\win-arm64,\runtimes\win-x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\LICENSE-EXCEPTION.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
; The MSFS 2024 Community packages (removed again by the uninstaller).
Source: "{#Root}\toolbar\opensky-atc-toolbar\Packages\opensky-atc-toolbar\*"; DestDir: "{code:CommunityDir}\opensky-atc-toolbar"; Tasks: packages; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Root}\toolbar\opensky-atc-taxi-arrows\Packages\opensky-atc-taxi-arrows\*"; DestDir: "{code:CommunityDir}\opensky-atc-taxi-arrows"; Tasks: packages; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\OpenSkyATC.exe"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\OpenSkyATC.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenSkyATC.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
var
  CommunityPage: TInputDirWizardPage;

{ The Community folder from the sim's UserCfg.opt (line: InstalledPackagesPath "D:\MSFS"), or ''. }
function CommunityFromUserCfg(const Path: String): String;
var
  Lines: TArrayOfString;
  I, P: Integer;
  Line: String;
begin
  Result := '';
  if not LoadStringsFromFile(Path, Lines) then exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    if Pos('InstalledPackagesPath', Line) = 1 then
    begin
      P := Pos('"', Line);
      if P > 0 then
      begin
        Line := Copy(Line, P + 1, Length(Line));
        P := Pos('"', Line);
        if P > 0 then Line := Copy(Line, 1, P - 1);
        Result := AddBackslash(Line) + 'Community';
        exit;
      end;
    end;
  end;
end;

function FindCommunity(): String;
begin
  { Microsoft Store / Xbox app version, then Steam. }
  Result := CommunityFromUserCfg(ExpandConstant('{localappdata}\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt'));
  if Result = '' then
    Result := CommunityFromUserCfg(ExpandConstant('{userappdata}\Microsoft Flight Simulator 2024\UserCfg.opt'));
  if (Result = '') and DirExists(ExpandConstant('{localappdata}\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community')) then
    Result := ExpandConstant('{localappdata}\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community');
end;

function CommunityDir(Param: String): String;
begin
  Result := CommunityPage.Values[0];
end;

function SdkInstalled(): Boolean;
var
  Root: String;
begin
  Root := GetEnv('MSFS2024_SDK');
  if (Root = '') and not RegQueryStringValue(HKCU, 'Environment', 'MSFS2024_SDK', Root) then
    RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'MSFS2024_SDK', Root);
  Result := ((Root <> '') and FileExists(AddBackslash(Root) + 'SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll'))
    or FileExists('C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll');
end;

procedure InitializeWizard();
begin
  CommunityPage := CreateInputDirPage(wpSelectTasks, CustomMessage('CommunityTitle'), CustomMessage('CommunityDesc'),
    CustomMessage('CommunityPrompt'), False, '');
  CommunityPage.Add('');
  { /COMMUNITY="D:\...\Community" on the command line overrides the folder found (silent installs, tests). }
  CommunityPage.Values[0] := ExpandConstant('{param:COMMUNITY|}');
  if CommunityPage.Values[0] = '' then CommunityPage.Values[0] := FindCommunity();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = CommunityPage.ID) and not WizardIsTaskSelected('packages');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = CommunityPage.ID) and (Trim(CommunityPage.Values[0]) = '') then
  begin
    MsgBox(CustomMessage('CommunityPrompt'), mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardSilent() and not SdkInstalled() then
      MsgBox(CustomMessage('NoSdk'), mbInformation, MB_OK);
    if not WizardSilent() and WizardIsTaskSelected('packages') and (FindWindowByClassName('AceApp') <> 0) then
      MsgBox(CustomMessage('SimRunning'), mbInformation, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\OpenSkyATC'), True, True, True);
      DelTree(ExpandConstant('{userappdata}\OpenSkyATC'), True, True, True);
    end;
end;
