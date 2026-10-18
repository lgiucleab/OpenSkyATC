; OpenSkyATC installer (step 17), built with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
; Build with: python installer/build.py   (publishes the app, assembles the MSFS packages, runs ISCC)
; Writes artifacts/installer/OpenSkyATC-Setup-<version>.exe. Installing also creates the uninstaller
; (unins000.exe, "Uninstall OpenSkyATC" in the Start menu and in Windows Settings > Apps).
; Brand images: branding/ (python branding/tools/generate_branding.py). Every page has its own text, in English
; and Spanish; light or dark follows Windows (owner's choice).

#ifndef AppVersion
  #define AppVersion "1.0.0-alpha"
#endif
; Windows' file version must be numbers only: "1.0.0-alpha" -> 1.0.0.
#if Pos("-", AppVersion) > 0
  #define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define NumericVersion AppVersion
#endif
#define AppName "OpenSkyATC"
#define Root ".."
#define Brand "..\branding"
#define RepoUrl "https://github.com/lgiucleab/OpenSkyATC"

[Setup]
AppId={{6F1C2B7E-4E0A-4C55-9B7E-2C9D1A0F5E31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=OpenSkyATC contributors
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=OpenSkyATC contributors, GPL-3.0
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
; Per-user install: no administrator rights, and the sim's Community folder is in the user's profile too.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
LicenseFile={#Root}\LICENSE
OutputDir={#Root}\artifacts\installer
OutputBaseFilename=OpenSkyATC-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\OpenSkyATC.exe
CloseApplications=yes
ShowLanguageDialog=auto

; ---- Look: the brand, light or dark like Windows.
SetupIconFile={#Brand}\OpenSkyATC.ico
WizardStyle=modern dynamic
WizardSizePercent=110
WizardImageFile={#Brand}\installer\wizard-large-100.png,{#Brand}\installer\wizard-large-125.png,{#Brand}\installer\wizard-large-150.png,{#Brand}\installer\wizard-large-175.png,{#Brand}\installer\wizard-large-200.png,{#Brand}\installer\wizard-large-250.png
WizardSmallImageFile={#Brand}\installer\wizard-small-100.png,{#Brand}\installer\wizard-small-125.png,{#Brand}\installer\wizard-small-150.png,{#Brand}\installer\wizard-small-175.png,{#Brand}\installer\wizard-small-200.png,{#Brand}\installer\wizard-small-250.png
; dark mode has its own image settings: the same brand images
WizardImageFileDynamicDark={#Brand}\installer\wizard-large-100.png,{#Brand}\installer\wizard-large-125.png,{#Brand}\installer\wizard-large-150.png,{#Brand}\installer\wizard-large-175.png,{#Brand}\installer\wizard-large-200.png,{#Brand}\installer\wizard-large-250.png
WizardSmallImageFileDynamicDark={#Brand}\installer\wizard-small-100.png,{#Brand}\installer\wizard-small-125.png,{#Brand}\installer\wizard-small-150.png,{#Brand}\installer\wizard-small-175.png,{#Brand}\installer\wizard-small-200.png,{#Brand}\installer\wizard-small-250.png
WizardImageBackColorDynamicDark=#10182A
WizardImageStretch=yes
WizardImageBackColor=#10182A

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
; ---- Every page, in OpenSkyATC's words.
english.SetupWindowTitle=%1 Setup
spanish.SetupWindowTitle=Instalación de %1
english.SelectLanguageTitle=OpenSkyATC
spanish.SelectLanguageTitle=OpenSkyATC
english.SelectLanguageLabel=Choose the language for the installer (the app lets you pick English or Spanish later).
spanish.SelectLanguageLabel=Elige el idioma del instalador (en la aplicación podrás elegir inglés o español después).
english.WelcomeLabel1=Welcome to OpenSkyATC
spanish.WelcomeLabel1=Bienvenido a OpenSkyATC
english.WelcomeLabel2=Free, open-source air traffic control for Microsoft Flight Simulator 2024: real controller voices, talk back by voice or with buttons, taxi arrows in the sim, a toolbar panel and an end-of-flight debrief.%n%nThis installs [name/ver] for your Windows user. Everything runs on your PC: no accounts, no cloud, no telemetry.
spanish.WelcomeLabel2=Control de tráfico aéreo gratuito y de código abierto para Microsoft Flight Simulator 2024: voces de controlador, responde por voz o con botones, flechas de rodaje en el simulador, un panel en la barra de herramientas y un resumen al final del vuelo.%n%nSe instalará [name/ver] para tu usuario de Windows. Todo funciona en tu PC: sin cuentas, sin nube, sin telemetría.
english.WizardLicense=Free software licence
spanish.WizardLicense=Licencia de software libre
english.LicenseLabel=OpenSkyATC is free software under the GNU GPL version 3.
spanish.LicenseLabel=OpenSkyATC es software libre bajo la GNU GPL versión 3.
english.LicenseLabel3=You may use, share and change OpenSkyATC under these terms. Please read them and accept to continue.
spanish.LicenseLabel3=Puedes usar, compartir y modificar OpenSkyATC según estos términos. Léelos y acéptalos para continuar.
english.WizardSelectDir=Where to install
spanish.WizardSelectDir=Dónde instalar
english.SelectDirDesc=Choose the folder for the OpenSkyATC app.
spanish.SelectDirDesc=Elige la carpeta para la aplicación OpenSkyATC.
english.SelectDirLabel3=OpenSkyATC goes in this folder (no administrator rights needed). Your settings, voices and debriefs are kept separately, in your user profile.
spanish.SelectDirLabel3=OpenSkyATC se instala en esta carpeta (no hacen falta permisos de administrador). Tu configuración, voces y resúmenes se guardan aparte, en tu perfil de usuario.
english.SelectDirBrowseLabel=Click Next to keep this folder, or Browse to choose another one.
spanish.SelectDirBrowseLabel=Haz clic en Siguiente para usar esta carpeta, o en Examinar para elegir otra.
english.WizardSelectTasks=Options
spanish.WizardSelectTasks=Opciones
english.SelectTasksDesc=What should be installed with the app?
spanish.SelectTasksDesc=¿Qué se instala junto con la aplicación?
english.SelectTasksLabel2=The sim packages add the OpenSkyATC panel to the MSFS 2024 toolbar and the blue taxi arrows. Keep them unless you install them yourself.
spanish.SelectTasksLabel2=Los paquetes del simulador añaden el panel de OpenSkyATC a la barra de herramientas de MSFS 2024 y las flechas azules de rodaje. Déjalos marcados salvo que los instales tú.
english.WizardReady=Ready for departure
spanish.WizardReady=Listo para la salida
english.ReadyLabel1=Everything is set. OpenSkyATC will be installed with these choices:
spanish.ReadyLabel1=Todo listo. OpenSkyATC se instalará con estas opciones:
english.ReadyLabel2a=Click Install to start, or Back to change something.
spanish.ReadyLabel2a=Haz clic en Instalar para empezar, o en Atrás para cambiar algo.
english.ReadyLabel2b=Click Install to start.
spanish.ReadyLabel2b=Haz clic en Instalar para empezar.
english.WizardInstalling=Installing OpenSkyATC
spanish.WizardInstalling=Instalando OpenSkyATC
english.InstallingLabel=Copying the app, its voices engine and the sim packages. This takes a moment.
spanish.InstallingLabel=Copiando la aplicación, su motor de voz y los paquetes del simulador. Tarda un momento.
english.FinishedHeadingLabel=OpenSkyATC is ready
spanish.FinishedHeadingLabel=OpenSkyATC está listo
english.FinishedLabelNoIcons=OpenSkyATC is installed.%n%nTo fly: open Settings and enter your SimBrief Pilot ID (or choose a .pln), load a flight at a gate in MSFS 2024, then press Import plan. The ATC voices and the speech model download once, the first time.
spanish.FinishedLabelNoIcons=OpenSkyATC está instalado.%n%nPara volar: abre Configuración e introduce tu ID de piloto de SimBrief (o elige un .pln), carga un vuelo en una puerta en MSFS 2024 y pulsa Importar plan. Las voces del ATC y el modelo de voz se descargan una vez, la primera vez.
english.FinishedLabel=OpenSkyATC is installed; find it in the Start menu.%n%nTo fly: open Settings and enter your SimBrief Pilot ID (or choose a .pln), load a flight at a gate in MSFS 2024, then press Import plan. The ATC voices and the speech model download once, the first time.
spanish.FinishedLabel=OpenSkyATC está instalado; lo encontrarás en el menú Inicio.%n%nPara volar: abre Configuración e introduce tu ID de piloto de SimBrief (o elige un .pln), carga un vuelo en una puerta en MSFS 2024 y pulsa Importar plan. Las voces del ATC y el modelo de voz se descargan una vez, la primera vez.
english.ClickFinish=Have a good flight!
spanish.ClickFinish=¡Buen vuelo!
english.ExitSetupMessage=OpenSkyATC is not installed yet. You can run this installer again at any time.%n%nExit the installer?
spanish.ExitSetupMessage=OpenSkyATC aún no está instalado. Puedes volver a ejecutar este instalador cuando quieras.%n%n¿Salir del instalador?
english.ConfirmUninstall=Remove OpenSkyATC, its toolbar panel and taxi arrows from this PC?
spanish.ConfirmUninstall=¿Quitar OpenSkyATC, su panel de la barra de herramientas y las flechas de rodaje de este PC?
english.UninstallStatusLabel=Removing OpenSkyATC and its sim packages...
spanish.UninstallStatusLabel=Quitando OpenSkyATC y sus paquetes del simulador...
english.UninstalledAll=OpenSkyATC was removed. Thank you for flying with it!
spanish.UninstalledAll=OpenSkyATC se ha quitado. ¡Gracias por volar con él!

[CustomMessages]
english.PackagesTask=Toolbar panel and taxi arrows for MSFS 2024 (in the sim's Community folder)
spanish.PackagesTask=Panel de la barra de herramientas y flechas de rodaje para MSFS 2024 (en la carpeta Community del simulador)
english.ShortcutsGroup=Shortcuts:
spanish.ShortcutsGroup=Accesos directos:
english.DesktopTask=A desktop icon
spanish.DesktopTask=Un icono en el escritorio
english.CommunityTitle=The sim's Community folder
spanish.CommunityTitle=La carpeta Community del simulador
english.CommunityDesc=Where MSFS 2024 looks for add-ons.
spanish.CommunityDesc=Donde MSFS 2024 busca los complementos.
english.CommunityPrompt=This is your Community folder, found from the sim's settings. Change it only if you moved it:
spanish.CommunityPrompt=Esta es tu carpeta Community, encontrada en la configuración del simulador. Cámbiala solo si la moviste:
english.CommunityMissing=Choose the Community folder, or go Back and untick the sim packages.
spanish.CommunityMissing=Elige la carpeta Community, o vuelve Atrás y desmarca los paquetes del simulador.
english.SimRunning=MSFS 2024 is running: restart the sim to see the OpenSkyATC panel and arrows.
spanish.SimRunning=MSFS 2024 está en marcha: reinicia el simulador para ver el panel y las flechas de OpenSkyATC.
english.DeleteData=Also delete OpenSkyATC's downloaded voices and speech models, its settings and your flight debriefs?
spanish.DeleteData=¿Borrar también las voces y modelos de voz descargados, la configuración y los resúmenes de vuelo de OpenSkyATC?
english.UninstallShortcut=Uninstall OpenSkyATC
spanish.UninstallShortcut=Desinstalar OpenSkyATC
english.LaunchApp=Start OpenSkyATC now
spanish.LaunchApp=Iniciar OpenSkyATC ahora
english.OpenGuide=Open the getting-started guide (GitHub)
spanish.OpenGuide=Abrir la guía de inicio (GitHub)
english.MemoApp=App
spanish.MemoApp=Aplicación
english.MemoPackages=Sim packages (toolbar panel, taxi arrows)
spanish.MemoPackages=Paquetes del simulador (panel, flechas de rodaje)
english.MemoNoPackages=Sim packages: not installed
spanish.MemoNoPackages=Paquetes del simulador: no se instalan
english.MemoIncluded=Included:%n   .NET runtime, Microsoft SimConnect, Piper and Whisper engines
spanish.MemoIncluded=Incluido:%n   entorno .NET, Microsoft SimConnect, motores Piper y Whisper
english.MemoDownloads=Downloaded on first use:%n   ATC voices (about 70 MB each), speech model (about 140 MB)
spanish.MemoDownloads=Se descarga al usarlo por primera vez:%n   voces del ATC (unos 70 MB cada una), modelo de voz (unos 140 MB)
english.MemoShortcuts=Start menu entry
spanish.MemoShortcuts=Entrada en el menú Inicio
english.MemoDesktop=, desktop icon
spanish.MemoDesktop=, icono en el escritorio

[Tasks]
Name: "packages"; Description: "{cm:PackagesTask}"
Name: "desktopicon"; Description: "{cm:DesktopTask}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked

[Files]
; The app, .NET included (self-contained); only the x64 natives are needed. build.py has put Microsoft's SimConnect
; DLLs next to it (shipped in the installer only, under Microsoft's terms; see LICENSE-EXCEPTION.md).
Source: "{#Root}\artifacts\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb,\runtimes\win-arm64,\runtimes\win-x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\LICENSE-EXCEPTION.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
; The MSFS 2024 Community packages (removed again by the uninstaller).
Source: "{#Root}\toolbar\opensky-atc-toolbar\Packages\opensky-atc-toolbar\*"; DestDir: "{code:CommunityDir}\opensky-atc-toolbar"; Tasks: packages; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Root}\toolbar\opensky-atc-taxi-arrows\Packages\opensky-atc-taxi-arrows\*"; DestDir: "{code:CommunityDir}\opensky-atc-taxi-arrows"; Tasks: packages; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\OpenSkyATC.exe"; Comment: "ATC for Microsoft Flight Simulator 2024"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"; IconFilename: "{app}\OpenSkyATC.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\OpenSkyATC.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenSkyATC.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
Filename: "{#RepoUrl}#install"; Description: "{cm:OpenGuide}"; Flags: shellexec postinstall skipifsilent unchecked

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
    MsgBox(CustomMessage('CommunityMissing'), mbError, MB_OK);
    Result := False;
  end;
end;

{ The "Ready for departure" summary: what goes where, and what downloads later. }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Shortcuts: String;
begin
  Result := CustomMessage('MemoApp') + NewLine + Space + WizardDirValue() + NewLine + NewLine;
  if WizardIsTaskSelected('packages') then
    Result := Result + CustomMessage('MemoPackages') + NewLine + Space + CommunityPage.Values[0] + NewLine + NewLine
  else
    Result := Result + CustomMessage('MemoNoPackages') + NewLine + NewLine;
  Shortcuts := CustomMessage('MemoShortcuts');
  if WizardIsTaskSelected('desktopicon') then Shortcuts := Shortcuts + CustomMessage('MemoDesktop');
  Result := Result + Shortcuts + NewLine + NewLine + CustomMessage('MemoIncluded') + NewLine + CustomMessage('MemoDownloads');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardSilent() and WizardIsTaskSelected('packages') and (FindWindowByClassName('AceApp') <> 0) then
    MsgBox(CustomMessage('SimRunning'), mbInformation, MB_OK);
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
