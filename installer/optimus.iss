; ============================================================================
;  Optimus - script d'installation (Inno Setup 6.1 ou plus recent)
;
;  Ne se compile pas seul : tools\build-installer.ps1 publie l'application,
;  puis appelle ISCC en lui passant la version et le dossier publie. Compiler
;  ce fichier a la main produirait un installateur bati sur une publication
;  dont personne ne sait de quand elle date, et c'est exactement le genre de
;  paquet qu'on diffuse par erreur.
;
;  Quatre choix structurent ce script.
;
;  1. INSTALLATION PAR UTILISATEUR. PrivilegesRequired=lowest : pas d'UAC, pas
;     de droits administrateur, tout va dans %LOCALAPPDATA%\Programs\Optimus.
;     Optimus n'installe aucun service, ne touche a aucun reglage systeme et
;     n'a donc rien a faire dans Program Files. Demander l'elevation pour cela
;     serait reclamer un pouvoir dont on ne se sert pas.
;
;  2. LES DONNEES NE SONT PAS LE PROGRAMME. Le programme va dans le dossier
;     d'installation, remplace a chaque mise a jour. Les touches, les macros,
;     les formulations apprises et Piper vivent dans %APPDATA%\Optimus et n'y
;     sont jamais touches (D35, D43, D46). La desinstallation le respecte : ce
;     qui appartient au pilote lui reste, sauf demande explicite.
;
;  3. LE BANC D'ESSAI N'EST PAS DISTRIBUE. Optimus.Cli est publie en fichier
;     autonome, soit 41 Mo qu'Inno embarquerait dans l'archive QUE LE PILOTE
;     LE COCHE OU NON : la selection de composants ne change que ce qui est
;     installe, jamais ce qui est telecharge. Pour un banc de diagnostic dont
;     l'application couvre desormais toutes les fonctions - import des
;     keybinds, assignations, journaux - c'etaient 41 Mo imposes a tout le
;     monde. Il reste construit par tools\publish-cli.ps1, pour qui developpe.
;
;  4. PIPER EST TELECHARGE, PAS EMBARQUE. 37 Mo de moteur et 60 Mo par voix,
;     pour une fonction dont on peut se passer : les embarquer ferait porter
;     260 Mo a tout le monde, y compris a qui n'en veut pas. Chaque
;     telechargement est verifie par son empreinte SHA-256, relevee le
;     2026-08-27 sur les fichiers reellement essayes.
; ============================================================================

#ifndef Version
  #define Version "0.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\Optimus.App"
#endif

#define AppName "Optimus"
#define Publisher "Optimus"

; ------------------------------------------------------------- le plugin Stream Deck
;
; Un plugin Stream Deck est un dossier, pas un programme : l'application Elgato le
; trouve en demarrant s'il porte le bon nom au bon endroit. Rien a compiler, rien a
; enregistrer, rien qui passe par la boutique d'Elgato.
;
; Il est EMBARQUE et non telecharge, contrairement a Piper : 510 Ko contre 37 Mo, et
; il n'a pas de version propre a suivre.
; Le dossier et le nom separement : un antislash colle au guillemet fermant passe
; pour une echappement chez le preprocesseur, et la definition part de travers.
#define PluginId "com.optimus.copilot.sdPlugin"
#define PluginRoot "..\tools\streamdeck"

; ------------------------------------------------------------- telechargements
#define PiperUrl "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"
#define PiperHash "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea"
#define VoiceBase "https://huggingface.co/rhasspy/piper-voices/resolve/main"

#define WhisperUrl "https://github.com/ggml-org/whisper.cpp/releases/download/b4938/whisper-bin-x64.zip"
#define WhisperHash "c2a4b60edb11f7e11a9191ffb50929535527d4d91c9903dbe3e554583bbbc63d"
#define ModelUrl "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
#define ModelHash "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"

[Setup]
AppId={{7E2C4A61-8D3F-4B5E-9C1A-0F6D2B8E4A73}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#Publisher}
VersionInfoVersion={#Version}

; Par utilisateur : ni UAC, ni droits administrateur. {autopf} se resout alors
; en %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

OutputDir=..\publish
OutputBaseFilename=Optimus-{#Version}-installateur
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; L'icone de l'installateur lui-meme : c'est elle que le pilote voit dans ses
; telechargements, avant d'avoir rien installe. La petite image en tete d'assistant est
; fournie en quatre tailles, qu'Inno choisit selon la densite de l'ecran. Elle doit etre
; du BMP sans transparence, d'ou le logo aplati sur blanc par tools\make-icon.ps1.
SetupIconFile=..\images\Optimus.ico
WizardSmallImageFile=..\images\wizard-small.bmp,..\images\wizard-small@1.5x.bmp,..\images\wizard-small@2x.bmp,..\images\wizard-small@2.5x.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 2004 : c'est ce qu'exige le moteur de reconnaissance vocale. Le
; verifier ici evite une installation qui reussit puis une application qui ne
; demarre pas.
MinVersion=10.0.19041

UninstallDisplayName={#AppName} {#Version}
UninstallDisplayIcon={app}\Optimus.App.exe

; L'assistant parle anglais, pour tout le monde.
;
; Ce n'est pas une preference : c'est la seule langue qu'on puisse presumer d'un pilote
; de Star Citizen, jeu qui n'existe qu'en anglais. Offrir le francais a qui a un Windows
; francais aurait paru attentionne, mais aurait surtout produit deux assistants a tenir
; a jour pour un ecran qu'on voit une fois.
;
; L'APPLICATION, elle, se choisit sa langue : c'est la qu'on passe du temps.
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.ComponentApp=Optimus (required)
en.ComponentPiper=Local neural voice (Piper): engine, 37 MB downloaded
en.ComponentTom=Tom voice: French, male, medium quality (60 MB)
en.ComponentGilles=Gilles voice: French, male, faster (60 MB)
en.ComponentSiwis=Siwis voice: French, female, medium quality (60 MB)
en.ComponentRyan=Ryan voice: English, male, medium quality (60 MB)
en.ComponentAlan=Alan voice: British English, male, medium quality (60 MB)
en.ComponentAmy=Amy voice: English, female, medium quality (60 MB)
en.ComponentWhisper=Free speech (Whisper): understand what isn't a command (161 MB)
en.ComponentStreamDeck=Stream Deck plugin: five keys that drive Optimus and show its state (0.5 MB, nothing downloaded)
en.TaskDesktop=Create a desktop shortcut
en.DownloadFailed=The download failed.%n%n%1%n%nOptimus will install without the neural voices: Windows voices take over, and you can add Piper later.
en.ExtractFailed=The Piper archive could not be opened. Optimus will install without the neural voices.
en.StreamDeckDone=The Stream Deck plugin is installed, in:%n%1%n%nQuit the Stream Deck application and open it again: it only looks for plugins when it starts. The five Optimus actions then appear under an "Optimus" category.%n%nPaste the local API token into any Optimus key: Settings, Local API.
en.StreamDeckAbsent=The Stream Deck plugin was not installed: no Stream Deck application was found on this machine.%n%nNothing was written where it does not belong. Install the Stream Deck software, then run this installer again to add the plugin.
en.PurgeData=Also delete your data?%n%nThis would erase your key bindings, your macros, the phrasings Optimus has learned, and the Piper voices you downloaded, in:%n%1%n%nAnswer No to keep them for a reinstall.

[Types]
Name: "standard"; Description: "Optimus alone — nothing to download"
Name: "complet"; Description: "Complete: one voice per language and free speech (~320 MB downloaded)"
Name: "perso"; Description: "Custom installation"; Flags: iscustom

; « ExtraDiskSpaceRequired » n'est pas une coquetterie : Inno calcule l'espace annonce a
; partir des fichiers EMBARQUES, et ces composants-la sont telecharges par le code. Sans ces
; nombres, l'assistant annoncait obstinement 79,6 Mo quoi qu'on coche — un pilote pouvait
; demander 380 Mo en lisant « 79,6 ». Tailles relevees sur disque apres une installation
; reelle, le 2026-08-28.
[Components]
Name: "app"; Description: "{cm:ComponentApp}"; Types: complet standard perso; Flags: fixed
Name: "piper"; Description: "{cm:ComponentPiper}"; Types: complet; ExtraDiskSpaceRequired: 39415482
Name: "piper\tom"; Description: "{cm:ComponentTom}"; Types: complet; ExtraDiskSpaceRequired: 63515997
Name: "piper\gilles"; Description: "{cm:ComponentGilles}"; ExtraDiskSpaceRequired: 63515997
Name: "piper\siwis"; Description: "{cm:ComponentSiwis}"; ExtraDiskSpaceRequired: 63515997
Name: "piper\ryan"; Description: "{cm:ComponentRyan}"; Types: complet; ExtraDiskSpaceRequired: 63515997
Name: "piper\alan"; Description: "{cm:ComponentAlan}"; ExtraDiskSpaceRequired: 63515997
Name: "piper\amy"; Description: "{cm:ComponentAmy}"; ExtraDiskSpaceRequired: 63515997
Name: "whisper"; Description: "{cm:ComponentWhisper}"; Types: complet; ExtraDiskSpaceRequired: 169165161

; Dans AUCUN type, donc jamais coche d'office : il faut le vouloir. C'est la seule
; facon d'eviter qu'une installation « complete » ne creuse une arborescence Elgato
; chez un pilote qui n'a jamais eu de boitier.
;
; La premiere version cochait la case dans « Complete » et gardait un « Check » pour ne
; rien ecrire faute de Stream Deck. Mauvaise idee : Inno evalue ce Check AUSSI quand il
; calcule l'espace requis, si bien que la case pesait zero et que le total affiche ne
; bougeait pas en la cochant. Une case qui ne change rien a l'ecran passe pour une case
; qui ne fait rien.
;
; Cocher installe, decocher n'installe pas. Rien d'autre a comprendre - et le poids suit.
Name: "streamdeck"; Description: "{cm:ComponentStreamDeck}"

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Optimus.App.exe"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "{#SourceDir}\Lancer-Optimus.cmd"; DestDir: "{app}"; Components: app; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\VERSION.txt"; DestDir: "{app}"; Components: app; Flags: ignoreversion skipifsourcedoesntexist
; Le catalogue, les touches par defaut du jeu et les repliques appartiennent a Optimus :
; ils sont remplaces a chaque mise a jour, c'est ainsi que les nouvelles commandes arrivent.
Source: "{#SourceDir}\data\*"; DestDir: "{app}\data"; Components: app;     Excludes: "profiles\default.json,copilots\*\copilot.json,copilots\*\personality.json";     Flags: ignoreversion recursesubdirs createallsubdirs

; Ces trois-la appartiennent au PILOTE : l'ecran de reglages y ecrit son mot d'eveil, sa voix,
; ses curseurs de caractere et ses reglages d'etages facultatifs. « onlyifdoesntexist » les pose
; a la premiere installation et n'y touche plus jamais.
;
; Mesure du 2026-08-28, avant correction : une mise a jour remettait le mot d'eveil a « Optimus »
; et l'humour a 40, effacant tout ce que le pilote avait regle. C'est la meme lecon que D35, D43,
; D46 et D70 — ce que le pilote change ne doit pas vivre la ou la publication ecrit — appliquee
; cette fois du cote de l'installateur.
;
; Sans risque pour les nouveautes : les chargeurs tolerent les sections absentes et leur donnent
; leur valeur par defaut. Un profil ecrit avant que « whisper » existe se lit tres bien.
Source: "{#SourceDir}\data\profiles\default.json"; DestDir: "{app}\data\profiles";     Components: app; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#SourceDir}\data\copilots\optimus\copilot.json"; DestDir: "{app}\data\copilots\optimus";     Components: app; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#SourceDir}\data\copilots\optimus\personality.json"; DestDir: "{app}\data\copilots\optimus";     Components: app; Flags: onlyifdoesntexist uninsneveruninstall

; Le plugin ne va pas dans le dossier d'installation : l'application Elgato ne regarde
; que le sien. Il est donc pose ailleurs, et retire a la desinstallation - « filesandordirs »
; plutot que la simple reprise des fichiers, pour emporter aussi ce qu'une copie manuelle
; anterieure aurait laisse a cote.
Source: "{#PluginRoot}\{#PluginId}\*"; DestDir: "{userappdata}\Elgato\StreamDeck\Plugins\{#PluginId}";     Components: streamdeck;     Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Elgato\StreamDeck\Plugins\{#PluginId}"; Components: streamdeck

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Optimus.App.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Optimus.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Optimus.App.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

{ Dossier des donnees du pilote. Doit correspondre exactement a
  PiperInstallation.DefaultRoot, sans quoi Optimus chercherait Piper ailleurs
  que la ou l'installateur vient de le poser. }
function PiperRoot(): String;
begin
  Result := ExpandConstant('{userappdata}\Optimus\piper');
end;

function DataRoot(): String;
begin
  Result := ExpandConstant('{userappdata}\Optimus');
end;

{ Doit correspondre exactement a WhisperInstallation.DefaultRoot, sans quoi Optimus chercherait
  le moteur ailleurs que la ou l'installateur vient de le poser. }
{ Le Stream Deck est-il installe ?

  Deux indices, et il en suffit d'un. Le dossier de donnees est cree des le premier
  demarrage de l'application et survit a sa mise a jour ; l'executable, lui, repond
  encore quand ce dossier a ete efface. Chercher les deux evite de conclure « absent »
  sur une installation neuve qu'on n'a pas encore lancee, ou sur un profil nettoye. }
function StreamDeckPresent(): Boolean;
begin
  Result := DirExists(ExpandConstant('{userappdata}\Elgato\StreamDeck'))
         or FileExists(ExpandConstant('{commonpf}\Elgato\StreamDeck\StreamDeck.exe'))
         or FileExists(ExpandConstant('{commonpf32}\Elgato\StreamDeck\StreamDeck.exe'));
end;

function StreamDeckPlugin(): String;
begin
  Result := ExpandConstant('{userappdata}\Elgato\StreamDeck\Plugins\{#PluginId}');
end;

function WhisperRoot(): String;
begin
  Result := ExpandConstant('{userappdata}\Optimus\whisper');
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

{ Met une voix en file, sauf si elle est deja posee. Rend le nombre de fichiers ajoutes.

  Le test compte lors des mises a jour : les composants deja choisis restent coches d'une
  installation a l'autre, et sans lui chaque mise a jour retelechargerait 60 Mo par voix pour
  rien. }
function QueueVoice(const Folder, Dataset, Quality, Name: String): Integer;
var
  Path: String;
begin
  Result := 0;

  if FileExists(PiperRoot() + '\voices\' + Name + '.onnx') then
    Exit;

  { << Folder >> porte la langue - << fr/fr_FR >>, << en/en_US >>. Elle vivait dans la
    base tant qu'Optimus ne parlait que francais ; l'y laisser aurait impose une base
    par langue, donc une troisieme le jour ou une troisieme langue arrive. }
  Path := '{#VoiceBase}/' + Folder + '/' + Dataset + '/' + Quality + '/' + Name;

  DownloadPage.Add(Path + '.onnx', Name + '.onnx', '');
  DownloadPage.Add(Path + '.onnx.json', Name + '.onnx.json', '');

  Result := 2;
end;

{ Extraction par tar.exe, present dans Windows depuis la version 1803, soit
  bien avant le minimum exige plus haut. L'archive place tout sous un dossier
  << piper\ >> : --strip-components=1 le retire, pour que piper.exe se retrouve
  a la racine que PiperInstallation.Locate inspecte. }
function ExtractPiper(const Archive, Destination: String): Boolean;
var
  Code: Integer;
begin
  ForceDirectories(Destination);

  Result := Exec(
    ExpandConstant('{sys}\tar.exe'),
    '-xf "' + Archive + '" -C "' + Destination + '" --strip-components=1',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function InstallVoice(const Name: String): Boolean;
var
  Voices: String;
begin
  Result := True;

  { Rien n'a ete telecharge : la voix etait deja posee. }
  if not FileExists(ExpandConstant('{tmp}\') + Name + '.onnx') then
    Exit;

  Voices := PiperRoot() + '\voices';
  ForceDirectories(Voices);

  { Les deux fichiers, ou aucun : Optimus refuse une voix dont la configuration
    manque, et la moitie d'une voix ne vaut pas mieux que rien. }
  Result :=
    CopyFile(ExpandConstant('{tmp}\') + Name + '.onnx', Voices + '\' + Name + '.onnx', False) and
    CopyFile(ExpandConstant('{tmp}\') + Name + '.onnx.json', Voices + '\' + Name + '.onnx.json', False);
end;

{ Met en file ce qui doit etre telecharge, puis le telecharge.

  Appelee depuis DEUX endroits, et ce n'est pas une maladresse : Inno n'appelle
  pas NextButtonClick en installation silencieuse - il n'y a pas de page a
  quitter - et le telechargement ne se serait jamais fait pour qui script son
  deploiement. PrepareToInstall, elle, est appelee dans les deux modes. }
{ Telecharge ce qui manque, et seulement ce qui manque.

  Rien n'est retelecharge s'il est deja la : c'est ce qui rend une mise a jour supportable.
  Inno preselectionne les composants choisis lors de l'installation precedente, si bien que
  sans ces tests un pilote qui a coche Whisper une fois retelechargerait 150 Mo a chaque
  nouvelle version, pour ecraser des fichiers identiques. }
procedure FetchExtras(const Interactive: Boolean);
var
  Queued: Integer;
begin
  DownloadPage.Clear;
  Queued := 0;

  if WizardIsComponentSelected('piper')
     and not FileExists(PiperRoot() + '\piper.exe') then
  begin
    DownloadPage.Add('{#PiperUrl}', 'piper.zip', '{#PiperHash}');
    Queued := Queued + 1;
  end;

  if WizardIsComponentSelected('piper\tom') then
    Queued := Queued + QueueVoice('fr/fr_FR', 'tom', 'medium', 'fr_FR-tom-medium');
  if WizardIsComponentSelected('piper\gilles') then
    Queued := Queued + QueueVoice('fr/fr_FR', 'gilles', 'low', 'fr_FR-gilles-low');
  if WizardIsComponentSelected('piper\siwis') then
    Queued := Queued + QueueVoice('fr/fr_FR', 'siwis', 'medium', 'fr_FR-siwis-medium');

  if WizardIsComponentSelected('piper\ryan') then
    Queued := Queued + QueueVoice('en/en_US', 'ryan', 'medium', 'en_US-ryan-medium');
  if WizardIsComponentSelected('piper\alan') then
    Queued := Queued + QueueVoice('en/en_GB', 'alan', 'medium', 'en_GB-alan-medium');
  if WizardIsComponentSelected('piper\amy') then
    Queued := Queued + QueueVoice('en/en_US', 'amy', 'medium', 'en_US-amy-medium');

  if WizardIsComponentSelected('whisper') then
  begin
    if not FileExists(WhisperRoot() + '\whisper-cli.exe') then
    begin
      DownloadPage.Add('{#WhisperUrl}', 'whisper.zip', '{#WhisperHash}');
      Queued := Queued + 1;
    end;

    if not FileExists(WhisperRoot() + '\models\ggml-base.bin') then
    begin
      DownloadPage.Add('{#ModelUrl}', 'ggml-base.bin', '{#ModelHash}');
      Queued := Queued + 1;
    end;
  end;

  { Tout est deja la : ni page de telechargement, ni attente. Une mise a jour sur une
    installation complete doit etre aussi rapide qu'une mise a jour sans options. }
  if Queued = 0 then
    Exit;

  if Interactive then
    DownloadPage.Show;

  try
    try
      DownloadPage.Download;
    except
      { Un telechargement rate ne doit pas faire echouer l'installation : Optimus fonctionne
        sans ces etages. On le dit, et on continue. Abandonner ici ramenerait le pilote en
        arriere sans rien installer du tout, pour des fonctions facultatives. }
      SuppressibleMsgBox(
        FmtMessage(CustomMessage('DownloadFailed'), [GetExceptionMessage]),
        mbInformation, MB_OK, IDOK);
    end;
  finally
    if Interactive then
      DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = wpReady)
     and (WizardIsComponentSelected('piper') or WizardIsComponentSelected('whisper')) then
    FetchExtras(True);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if WizardSilent
     and (WizardIsComponentSelected('piper') or WizardIsComponentSelected('whisper')) then
    FetchExtras(False);
end;

procedure InstallWhisper();
var
  Models: String;
begin
  if not FileExists(ExpandConstant('{tmp}\whisper.zip')) then
    Exit;

  { L'archive place tout sous « Release\ » : --strip-components=1 le retire, comme pour Piper. }
  if not ExtractPiper(ExpandConstant('{tmp}\whisper.zip'), WhisperRoot()) then
  begin
    SuppressibleMsgBox(CustomMessage('ExtractFailed'), mbInformation, MB_OK, IDOK);
    Exit;
  end;

  Models := WhisperRoot() + '\models';
  ForceDirectories(Models);

  CopyFile(ExpandConstant('{tmp}\ggml-base.bin'), Models + '\ggml-base.bin', False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  if WizardIsComponentSelected('whisper') then
    InstallWhisper();

  { Le dire, parce que rien ne le laisse deviner : l'application Elgato ne cherche ses
    plugins qu'au demarrage. Un pilote qui vient de cocher la case ouvrirait son Stream
    Deck, n'y verrait rien, et conclurait que l'installation a echoue. }
  if WizardIsComponentSelected('streamdeck') then
  begin
    if StreamDeckPresent() then
      SuppressibleMsgBox(FmtMessage(CustomMessage('StreamDeckDone'), [StreamDeckPlugin()]),
                         mbInformation, MB_OK, IDOK)
    else
      SuppressibleMsgBox(CustomMessage('StreamDeckAbsent'), mbInformation, MB_OK, IDOK);
  end;

  if not WizardIsComponentSelected('piper') then
    Exit;

  { L'archive n'est la que si le moteur manquait. Son absence signifie « deja installe »,
    pas « rien a faire » : sortir ici empecherait de poser une voix ajoutee a un Piper deja
    en place. Constate le 2026-08-28 — retirer une voix puis relancer l'installateur la
    telechargeait bien, mais ne la posait jamais. }
  if FileExists(ExpandConstant('{tmp}\piper.zip'))
     and not ExtractPiper(ExpandConstant('{tmp}\piper.zip'), PiperRoot()) then
  begin
    SuppressibleMsgBox(CustomMessage('ExtractFailed'), mbInformation, MB_OK, IDOK);
    Exit;
  end;

  if WizardIsComponentSelected('piper\tom') then
    InstallVoice('fr_FR-tom-medium');
  if WizardIsComponentSelected('piper\gilles') then
    InstallVoice('fr_FR-gilles-low');
  if WizardIsComponentSelected('piper\siwis') then
    InstallVoice('fr_FR-siwis-medium');
  if WizardIsComponentSelected('piper\ryan') then
    InstallVoice('en_US-ryan-medium');
  if WizardIsComponentSelected('piper\alan') then
    InstallVoice('en_GB-alan-medium');
  if WizardIsComponentSelected('piper\amy') then
    InstallVoice('en_US-amy-medium');
end;

{ Desinstallation : le programme s'en va, les donnees du pilote restent, sauf
  s'il demande le contraire. Effacer sans demander des assignations de touches
  patiemment construites serait le genre de perte qu'aucune reinstallation ne
  repare (RNF-11). Le defaut du dialogue est Non. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Root: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Root := DataRoot();

  if not DirExists(Root) then
    Exit;

  if SuppressibleMsgBox(
       FmtMessage(CustomMessage('PurgeData'), [Root]),
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(Root, True, True, True);
end;