using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Optimus.App.Input;
using Optimus.App.Mvvm;
using Optimus.Core.Abstractions;
using Optimus.Core.Bindings;
using Optimus.Core.Diagnostics;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Infrastructure.Game;
using Optimus.Infrastructure.Hosting;
using Optimus.Infrastructure.Speech;

namespace Optimus.App.ViewModels;

/// <summary>Ton d'une ligne de journal, pour la couleur.</summary>
public enum ActivityLevel
{
    Normal,
    Muted,
    Warning,
    Danger,
    Speech,
}

/// <summary>Une ligne du journal d'activité.</summary>
public sealed class ActivityEntry
{
    public required DateTimeOffset At { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    public ActivityLevel Level { get; init; }

    public string Time => At.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>Une commande du catalogue, telle que la liste l'affiche.</summary>
public sealed class CommandRow(CommandDefinition command, BindingProfile bindings)
{
    public CommandDefinition Command { get; } = command;

    public string Id => Command.Id;

    public string Name => Command.Name;

    public string Category => Command.Category;

    public string Phrases => string.Join(" · ", Command.AllPhrases.Take(4));

    public string Binding
    {
        get
        {
            if (Command.IsPassive)
            {
                return Localization.Localizer.T("Commands.SpokenOnly");
            }

            string? actionId = Command.ReferencedActionIds.FirstOrDefault();
            if (actionId is null)
            {
                return "—";
            }

            Lookup = bindings.Resolve(actionId, out Binding? binding);

            return Lookup switch
            {
                BindingLookup.Bound => binding!.Input.Describe(Localization.Localizer.Current),
                BindingLookup.NotBound => Localization.Localizer.T("Keys.Configurer"),
                BindingLookup.Unsupported => Localization.Localizer.T("Commands.NotInjectable"),
                _ => Localization.Localizer.T("Commands.UnknownAction"),
            };
        }
    }

    /// <summary>Ce que la recherche a rendu, retenu au passage.</summary>
    private BindingLookup Lookup { get; set; }

    /// <summary>
    /// La touche reste-t-elle à configurer ?
    ///
    /// Sur le RÉSULTAT de la recherche, et non sur le texte affiché. La version précédente
    /// comparait la chaîne à « à configurer » : traduire l'écran l'aurait rendue fausse en
    /// anglais, sans que rien ne le signale.
    /// </summary>
    public bool NeedsBinding
    {
        get
        {
            _ = Binding;
            return Lookup == BindingLookup.NotBound;
        }
    }
}

/// <summary>
/// Le tableau de bord.
///
/// Ne contient aucune logique de commande : tout passe par <see cref="OptimusRuntime"/>, partagé
/// avec le banc d'essai. Cette classe ne fait que traduire des événements en lignes affichables
/// et des clics en appels — c'est ce qui garantit que les deux interfaces se comportent pareil.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Au-delà, les plus anciennes lignes tombent : une session dure des heures.</summary>
    private const int JournalCapacity = 400;

    private readonly OptimusRuntime _runtime;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _detectorTimer;

    private string _utterance = string.Empty;
    private string _bindingFilter = string.Empty;
    private GameStatus _game = GameStatus.NotRunning;
    private bool _capturing;
    private string? _activeProfile;
    private string _profileName = string.Empty;
    private bool _switching;

    public MainViewModel(OptimusRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Tout ce que ce modele compose en C# est perime quand la langue change. « null »
        // veut dire « toutes les proprietes » pour WPF : c'est exactement ce qu'on a a dire,
        // et enumerer trente noms garantirait d'en oublier un au prochain ajout.
        Localization.Localizer.Changed += RefreshLanguage;

        _runtime.Activity += OnActivity;
        _runtime.MicrophoneOpen += OnMicrophone;
        _runtime.StateChanged += OnStateChanged;

        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);
        SendCommand = new AsyncRelayCommand(SendAsync, () => !string.IsNullOrWhiteSpace(Utterance));
        ToggleSimulationCommand = new RelayCommand(ToggleSimulation);
        ToggleKillSwitchCommand = new RelayCommand(ToggleKillSwitch);
        ClearJournalCommand = new RelayCommand(Journal.Clear);
        OpenLogsCommand = new RelayCommand(DiagnosticLog.Reveal);
        AssignCommand = new AsyncRelayCommand(AssignAsync, () => SelectedSlot is not null && !_capturing);
        UnassignCommand = new RelayCommand(Unassign, () => SelectedSlot?.Origin is not null);
        ImportLayoutCommand = new RelayCommand(ImportLayout);
        CreateProfileCommand = new RelayCommand(() => CreateProfile(null), CanName);
        DuplicateProfileCommand = new RelayCommand(() => CreateProfile(ActiveProfile), CanName);
        RenameProfileCommand = new RelayCommand(RenameProfile, CanName);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1);
        ExportLayoutCommand = new RelayCommand(ExportLayout);
        TestCommandCommand = new AsyncRelayCommand(TestCommandAsync, () => SelectedCommand is not null);
        Settings = new SettingsViewModel(_runtime, Add);
        MacroEditor = new MacroEditorViewModel(_runtime, Add, ReloadMacrosAsync);
        Understanding = new UnderstandingViewModel(_runtime, Add, ReloadMacrosAsync);

        // Le jeu peut demarrer ou s'arreter pendant la session : l'etat s'observe, il ne se
        // suppose pas. Deux secondes suffisent - c'est un affichage, pas une garde.
        _detectorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _detectorTimer.Tick += (_, _) => RefreshGame();
        _detectorTimer.Start();

        RefreshGame();
        RefreshCommands();
        RefreshProfiles();
        RefreshBindings();

        foreach (Core.Loading.LoadIssue issue in _runtime.Issues)
        {
            Add(new ActivityEntry
            {
                At = DateTimeOffset.UtcNow,
                Title = "anomalie de chargement",
                Detail = issue.ToString(),
                Level = ActivityLevel.Warning,
            });
        }
    }

    public ObservableCollection<ActivityEntry> Journal { get; } = new();

    public ObservableCollection<CommandRow> Commands { get; } = new();

    public ObservableCollection<ActionSlot> Bindings { get; } = new();

    /// <summary>
    /// Profil actif. L'affecter bascule réellement, sans redémarrer.
    ///
    /// Le garde-fou <c>_switching</c> n'est pas de la superstition : rafraîchir la liste
    /// réaffecte cette propriété, ce qui relancerait une bascule, qui rafraîchirait la liste.
    /// </summary>
    public string? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (_switching || value is null || !Set(ref _activeProfile, value))
            {
                return;
            }

            SwitchProfile(value);
        }
    }

    /// <summary>Nom saisi pour créer, dupliquer ou renommer.</summary>
    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (Set(ref _profileName, value))
            {
                CreateProfileCommand.RaiseCanExecuteChanged();
                DuplicateProfileCommand.RaiseCanExecuteChanged();
                RenameProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Ce qu'un profil recouvre, dit une fois pour toutes.
    ///
    /// La confusion naturelle est de croire qu'un profil contient les touches du jeu. Il ne
    /// contient que <b>vos</b> assignations : celles que vous avez posées ici ou importées
    /// depuis Star Citizen. Les touches par défaut du jeu ne changent pas d'un style de vol à
    /// l'autre, et les dupliquer dans chaque profil n'aurait rien apporté.
    /// </summary>
    public string ProfileHint => Localization.Localizer.T(
        "Keys.ProfileHint", _runtime.Overlay.Count, _runtime.BindingProfileName);

    public AsyncRelayCommand ToggleListeningCommand { get; }

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand ToggleSimulationCommand { get; }

    public RelayCommand ToggleKillSwitchCommand { get; }

    public RelayCommand ClearJournalCommand { get; }

    /// <summary>Ouvre le dossier des journaux : indispensable quand il faut envoyer un rapport.</summary>
    public RelayCommand OpenLogsCommand { get; }

    public AsyncRelayCommand AssignCommand { get; }

    public RelayCommand UnassignCommand { get; }

    public RelayCommand ImportLayoutCommand { get; }

    /// <summary>Crée un profil vide portant le nom saisi.</summary>
    public RelayCommand CreateProfileCommand { get; }

    /// <summary>Crée un profil qui reprend les assignations du profil courant.</summary>
    public RelayCommand DuplicateProfileCommand { get; }

    public RelayCommand RenameProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    /// <summary>Profils de touches installés.</summary>
    public ObservableCollection<string> Profiles { get; } = new();

    public RelayCommand ExportLayoutCommand { get; }

    public AsyncRelayCommand TestCommandCommand { get; }

    /// <summary>Les réglages, dans leur propre onglet.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>L'éditeur de macros.</summary>
    public MacroEditorViewModel MacroEditor { get; }

    /// <summary>Ce qu'Optimus n'a pas compris, et ce qu'on peut lui apprendre.</summary>
    public UnderstandingViewModel Understanding { get; }

    private Window? _owner;

    /// <summary>Fenêtre propriétaire, requise pour capturer une touche et ouvrir les dialogues.</summary>
    public Window? Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            Settings.Owner = value;
        }
    }

    public string CopilotName => _runtime.Copilot.Name;

    public string VoiceName =>
        _runtime.Copilot.Voice.VoiceId ?? Localization.Localizer.T("App.DefaultVoice");

    public string WakeWord => _runtime.Copilot.WakeWord;

    /// <summary>
    /// « Dites « Optimus, … » pour être entendu. »
    ///
    /// Composée ici plutôt que découpée en trois fragments dans le XAML. Le découpage tenait
    /// tant qu'il n'y avait qu'une langue : une autre ne remet pas les morceaux dans le même
    /// ordre, et le mot d'éveil n'y tombe pas au même endroit.
    /// </summary>
    public string WakeHint => Localization.Localizer.T("Cockpit.WakeHint", WakeWord);

    public string CatalogSummary =>
        Localization.Localizer.T(
            "Commands.CatalogSummary",
            _runtime.Catalog.Count,
            _runtime.Catalog.Commands.Sum(c => c.AllPhrases.Count()));

    /// <summary>
    /// Compte ce que la liste montre, et rien d'autre.
    ///
    /// Afficher les 627 actions du profil complet à côté d'une liste qui n'en montre que 68
    /// laissait croire à un filtre invisible.
    /// </summary>
    public string BindingSummary =>
        Localization.Localizer.T(
            "Keys.BindingSummary", Bindings.Count, Bindings.Count(s => s.IsBound))
        + (BlockingCount > 0
            ? Localization.Localizer.T("Keys.BindingBlocking", BlockingCount)
            : Localization.Localizer.T("Keys.BindingNoneBlocking"));

    public int BlockingCount => Bindings.Count(s => !s.IsBound && s.Need == ActionNeed.Primary);

    public string ListeningLabel =>
        Localization.Localizer.T(_runtime.IsListening ? "Cockpit.StopListening" : "Cockpit.Listen");

    public bool IsListening => _runtime.IsListening;

    public string ModeLabel =>
        Localization.Localizer.T(_runtime.SimulationMode ? "App.Simulation" : "App.RealMode");

    public bool IsReal => !_runtime.SimulationMode;

    public bool KillSwitch => _runtime.KillSwitch;

    public string KillSwitchLabel =>
        Localization.Localizer.T(_runtime.KillSwitch ? "App.Unblock" : "App.KillSwitch");

    public bool CombatActive => _runtime.State.CombatActive;

    public string GameLabel => _game.IsRunning
        ? Localization.Localizer.T("App.GameDetected",
            Localization.Localizer.T(_game.IsForeground ? "App.Foreground" : "App.Background"))
        : Localization.Localizer.T("App.GameNotDetected");

    public bool GameReady => _game.IsRunning && _game.IsForeground;

    public string Utterance
    {
        get => _utterance;
        set
        {
            if (Set(ref _utterance, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BindingFilter
    {
        get => _bindingFilter;
        set
        {
            if (Set(ref _bindingFilter, value))
            {
                RefreshBindings();
            }
        }
    }

    private ActionSlot? _selectedSlot;

    public ActionSlot? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (Set(ref _selectedSlot, value))
            {
                AssignCommand.RaiseCanExecuteChanged();
                UnassignCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private CommandRow? _selectedCommand;

    public CommandRow? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (Set(ref _selectedCommand, value))
            {
                TestCommandCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Préchauffe la voix et remplit la liste des voix installées.
    ///
    /// La première synthèse coûte jusqu'à 429 ms : la faire au démarrage évite qu'elle tombe sur
    /// la toute première phrase entendue, celle qui fait la première impression (D23).
    /// </summary>
    public async Task WarmUpAsync()
    {
        await _runtime.WarmUpAsync().ConfigureAwait(true);

        // L'API locale monte apres le prechauffage : elle n'est utile qu'une fois le moteur
        // pret, et la faire attendre evite qu'un client tres matinal tombe sur un copilote
        // encore en train de charger sa voix.
        _runtime.ApplyWhisperSettings();

        await _runtime.ApplyApiSettingsAsync().ConfigureAwait(true);
        await Settings.LoadVoicesAsync().ConfigureAwait(true);
    }

    private async Task ToggleListeningAsync()
    {
        if (_runtime.IsListening)
        {
            await _runtime.StopListeningAsync().ConfigureAwait(true);
            Add(Localization.Localizer.T("Log.ListeningStopped"), null, ActivityLevel.Muted);
            return;
        }

        try
        {
            await _runtime.StartListeningAsync().ConfigureAwait(true);
        }
        catch (MissingRecognizerException missing)
        {
            // Cet echec-la est PREVISIBLE : il suffit que Windows n'ait pas le module vocal de
            // la langue choisie. Le laisser remonter au filet a plantages le presentait comme
            // une defaillance d'Optimus, avec un message qui nommait la mauvaise langue.
            string advice = Localization.Localizer.T("Log.NoRecognizerHint", missing.Culture);

            Add(Localization.Localizer.T("Log.NoRecognizer", missing.Culture),
                advice, ActivityLevel.Warning);

            MessageBox.Show(advice, "Optimus", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Add(Localization.Localizer.T("Log.ListeningStarted"),
            Localization.Localizer.T("Log.ListeningStartedDetail",
                _runtime.RecognizerName, _runtime.GrammarSize, WakeWord),
            ActivityLevel.Normal);
    }

    private async Task SendAsync()
    {
        string text = Utterance.Trim();
        Utterance = string.Empty;

        await _runtime.HandleUtteranceAsync(text).ConfigureAwait(true);
    }

    private async Task TestCommandAsync()
    {
        if (SelectedCommand is null)
        {
            return;
        }

        await _runtime.RunCommandAsync(SelectedCommand.Command).ConfigureAwait(true);
    }

    private void ToggleSimulation()
    {
        bool goingReal = _runtime.SimulationMode;

        // Passer en réel envoie de vraies touches : cela se demande, cela ne se subit pas (§56).
        if (goingReal && MessageBox.Show(
                Localization.Localizer.T("Log.RealModeWarning"),
                Localization.Localizer.T("Log.RealModeTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        _runtime.SetSimulation(!goingReal);
        Add(Localization.Localizer.T(goingReal ? "Log.RealModeOn" : "Log.BackToSimulation"), null,
            goingReal ? ActivityLevel.Warning : ActivityLevel.Muted);
    }

    private void ToggleKillSwitch()
    {
        _runtime.SetKillSwitch(!_runtime.KillSwitch);

        Add(Localization.Localizer.T(_runtime.KillSwitch ? "Log.KillSwitchOn" : "Log.KillSwitchOff"), null,
            _runtime.KillSwitch ? ActivityLevel.Danger : ActivityLevel.Normal);
    }

    private async Task AssignAsync()
    {
        if (SelectedSlot is not ActionSlot slot || Owner is null)
        {
            return;
        }

        _capturing = true;
        AssignCommand.RaiseCanExecuteChanged();

        try
        {
            Add(Localization.Localizer.T("Log.PressKeyFor", slot.CommandName), Localization.Localizer.T("Log.EscapeToCancel"), ActivityLevel.Speech);

            InputSpec? captured = await WindowKeyCapture
                .CaptureAsync(Owner, TimeSpan.FromSeconds(20))
                .ConfigureAwait(true);

            if (captured is null)
            {
                Add(Localization.Localizer.T("Log.CaptureCancelled"), null, ActivityLevel.Muted);
                return;
            }

            string combination = BindingOverlay.Combination(captured);
            BindingAssignment? clash = _runtime.Overlay.Assignments
                .FirstOrDefault(a => BindingOverlay.Combination(a.Input) == combination);

            _runtime.Overlay.Assign(slot.ActionId, captured, AssignmentOrigin.Manual);
            _runtime.SaveOverlay();
            _runtime.ReloadBindings();

            Add($"{captured} → {slot.CommandName}",
                clash is null
                    ? Localization.Localizer.T("Log.RememberToExport")
                    : Localization.Localizer.T("Log.KeyAlreadyUsed", clash.ActionId),
                clash is null ? ActivityLevel.Normal : ActivityLevel.Warning);

            RefreshBindings();
            RefreshCommands();
        }
        finally
        {
            _capturing = false;
            AssignCommand.RaiseCanExecuteChanged();
        }
    }

    private void Unassign()
    {
        if (SelectedSlot is not ActionSlot slot || slot.Origin is null)
        {
            return;
        }

        _runtime.Overlay.Remove(slot.ActionId);
        _runtime.SaveOverlay();
        _runtime.ReloadBindings();

        Add(Localization.Localizer.T("Log.BindingRemoved", slot.CommandName),
            Localization.Localizer.T("Log.BindingRemovedHint"),
            ActivityLevel.Warning);

        RefreshBindings();
        RefreshCommands();
    }

    private void ImportLayout()
    {
        string? mappings = StarCitizenDetector.ResolveMappingsDirectory(_game.ExecutablePath);

        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = Localization.Localizer.T("Log.ImportDialogTitle"),
            Filter = Localization.Localizer.T("Log.ImportDialogFilter"),
            InitialDirectory = mappings is not null && Directory.Exists(mappings) ? mappings : string.Empty,
        };

        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        LayoutImport import = ScLayoutXml.Read(dialog.FileName);
        HashSet<string> needed = Bindings.Select(s => s.ActionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int adopted = 0;

        foreach (LayoutEntry entry in import.Entries)
        {
            // Le fichier du pilote couvre tout le jeu ; le catalogue n'en utilise qu'une part.
            if (!needed.Contains(entry.ActionId))
            {
                continue;
            }

            _runtime.Overlay.Assign(entry.ActionId, entry.Input, AssignmentOrigin.ImportedLayout);
            adopted++;
        }

        _runtime.SaveOverlay();
        _runtime.ReloadBindings();

        Add(Localization.Localizer.T("Log.Imported", adopted, import.LayoutName ?? Path.GetFileName(dialog.FileName)),
            import.Skipped.Count > 0 ? Localization.Localizer.T("Log.ImportSkipped", import.Skipped.Count, import.Skipped[0]) : null,
            ActivityLevel.Normal);

        RefreshBindings();
        RefreshCommands();
    }

    private void ExportLayout()
    {
        BindingAssignment[] manual = _runtime.Overlay.Assignments
            .Where(a => a.Origin == AssignmentOrigin.Manual)
            .ToArray();

        if (manual.Length == 0)
        {
            Add(Localization.Localizer.T("Log.NothingToExport"), Localization.Localizer.T("Log.NothingToExportHint"),
                ActivityLevel.Muted);
            return;
        }

        string? mappings = StarCitizenDetector.ResolveMappingsDirectory(_game.ExecutablePath);
        bool inGameFolder = mappings is not null && Directory.Exists(mappings);

        string target = inGameFolder
            ? Path.Combine(mappings!, "optimus.xml")
            : Path.Combine(Environment.CurrentDirectory, "optimus.xml");

        ScLayoutXml.Save(
            ScLayoutXml.Write(manual.Select(a => new LayoutEntry(a.ActionId, a.Input)), "optimus"),
            target);

        Add(Localization.Localizer.T("Log.Exported", manual.Length),
            inGameFolder
                ? Localization.Localizer.T("Log.ExportedInPlace")
                : Localization.Localizer.T("Log.ExportedElsewhere", target),
            ActivityLevel.Normal);
    }

    /// <summary>
    /// Reprend les macros après modification, et rafraîchit ce qui en dépend.
    ///
    /// Le catalogue change : la liste des commandes le reflète, et l'écoute repart pour que la
    /// grammaire connaisse les nouvelles formulations.
    /// </summary>
    private async Task ReloadMacrosAsync()
    {
        await _runtime.ReloadMacrosAsync().ConfigureAwait(true);

        RefreshCommands();
        RefreshBindings();
        Raise(nameof(CatalogSummary));
    }

    private void RefreshGame()
    {
        GameStatus status = _runtime.Detector.Detect();

        if (status.IsRunning == _game.IsRunning
            && status.IsForeground == _game.IsForeground
            && status.ProcessId == _game.ProcessId)
        {
            return;
        }

        _game = status;
        Raise(nameof(GameLabel));
        Raise(nameof(GameReady));
    }

    private void RefreshCommands()
    {
        Commands.Clear();

        foreach (CommandDefinition command in _runtime.Catalog.Commands
            .OrderBy(c => c.Category, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.CurrentCulture))
        {
            Commands.Add(new CommandRow(command, _runtime.Bindings));
        }

        Raise(nameof(CatalogSummary));
    }

    private void RefreshBindings()
    {
        IReadOnlyList<ActionSlot> all = BindingInventory.Build(
            _runtime.Catalog, _runtime.DefaultBindings, _runtime.Overlay);

        string? keep = SelectedSlot?.ActionId;

        Bindings.Clear();

        foreach (ActionSlot slot in BindingInventory.Search(all, BindingFilter))
        {
            Bindings.Add(slot);
        }

        SelectedSlot = Bindings.FirstOrDefault(s => s.ActionId == keep);

        Raise(nameof(BindingSummary));
        Raise(nameof(BlockingCount));
    }

    /// <summary>
    /// Relit la liste des profils depuis le disque, sans déclencher de bascule.
    ///
    /// Deux précautions, apprises à l'écran. La liste n'est reconstruite que si elle a
    /// réellement changé : vider puis regarnir fait perdre sa sélection à la liste déroulante,
    /// et la reposer dans la même passe ne prend pas — on obtient un sélecteur vide alors que
    /// le profil est bel et bien actif. Quand la reconstruction est inévitable, la sélection est
    /// réaffirmée à la passe suivante, une fois le changement de collection digéré.
    /// </summary>
    private void RefreshProfiles()
    {
        List<string> names = _runtime.BindingProfiles.Select(p => p.Name).ToList();

        // Le profil actif peut ne pas encore avoir de fichier : c'est le cas au tout premier
        // lancement, avant la premiere assignation. Il doit quand meme se voir dans la liste.
        if (!names.Contains(_runtime.BindingProfileName, StringComparer.Ordinal))
        {
            names.Insert(0, _runtime.BindingProfileName);
        }

        bool rebuilt = !Profiles.SequenceEqual(names, StringComparer.Ordinal);

        _switching = true;

        try
        {
            if (rebuilt)
            {
                Profiles.Clear();

                foreach (string name in names)
                {
                    Profiles.Add(name);
                }
            }

            _activeProfile = _runtime.BindingProfileName;
        }
        finally
        {
            _switching = false;
        }

        Raise(nameof(ActiveProfile));
        Raise(nameof(ProfileHint));
        DeleteProfileCommand.RaiseCanExecuteChanged();

        if (rebuilt)
        {
            _dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => Raise(nameof(ActiveProfile)));
        }
    }

    private bool CanName() => !string.IsNullOrWhiteSpace(ProfileName);

    private void SwitchProfile(string name)
    {
        try
        {
            _runtime.SwitchBindingProfile(name);

            Add(Localization.Localizer.T("Log.KeyProfile", _runtime.BindingProfileName),
                Localization.Localizer.T("Log.ProfileBindingsTaken", _runtime.Overlay.Count), ActivityLevel.Normal);
        }
        catch (Exception exception)
        {
            Add(Localization.Localizer.T("Log.ProfileSwitchFailed"), exception.Message, ActivityLevel.Warning);
        }

        RefreshProfiles();
        RefreshBindings();
        RefreshCommands();
    }

    /// <summary>Crée un profil, vide ou copié, puis bascule dessus.</summary>
    private void CreateProfile(string? copyFrom)
    {
        try
        {
            BindingProfileSet.Create(ProfileName, copyFrom);
        }
        catch (Exception exception)
        {
            Add(Localization.Localizer.T("Log.CreateFailed"), exception.Message, ActivityLevel.Warning);
            return;
        }

        string created = BindingProfileSet.Sanitize(ProfileName);

        Add(Localization.Localizer.T("Log.ProfileCreated", created),
            copyFrom is null
                ? Localization.Localizer.T("Log.ProfileEmpty")
                : Localization.Localizer.T("Log.ProfileCopiedFrom", copyFrom),
            ActivityLevel.Normal);

        ProfileName = string.Empty;
        SwitchProfile(created);
    }

    private void RenameProfile()
    {
        string from = _runtime.BindingProfileName;

        try
        {
            BindingProfileSet.Rename(from, ProfileName);
        }
        catch (Exception exception)
        {
            Add(Localization.Localizer.T("Log.RenameFailed"), exception.Message, ActivityLevel.Warning);
            return;
        }

        string to = BindingProfileSet.Sanitize(ProfileName);

        // La bascule suit le renommage : le moteur tient encore l'ancien chemin, qui n'existe
        // plus. Sans cela, la prochaine assignation recreerait un fichier au nom d'avant.
        _runtime.SwitchBindingProfile(to);

        Add(Localization.Localizer.T("Log.ProfileRenamed", to), Localization.Localizer.T("Log.ProfileWas", from), ActivityLevel.Normal);

        ProfileName = string.Empty;
        RefreshProfiles();
        RefreshBindings();
    }

    private void DeleteProfile()
    {
        string doomed = _runtime.BindingProfileName;

        try
        {
            BindingProfileSet.Delete(doomed);
        }
        catch (Exception exception)
        {
            Add(Localization.Localizer.T("Log.DeleteFailed"), exception.Message, ActivityLevel.Warning);
            return;
        }

        Add(Localization.Localizer.T("Log.ProfileDeleted", doomed), null, ActivityLevel.Warning);

        SwitchProfile(BindingProfileSet.Resolve(null));
    }

    private void OnActivity(object? sender, SessionActivity activity)
    {
        _dispatcher.BeginInvoke(() => Append(activity));
    }

    private void OnMicrophone(object? sender, bool open)
    {
        _dispatcher.BeginInvoke(() => Add(
            Localization.Localizer.T(open ? "Log.MicOpen" : "Log.MicClosed"), null, ActivityLevel.Muted));
    }

    /// <summary>
    /// Tout ce que l'écran doit relire quand la langue change.
    ///
    /// « null » suffit pour les propriétés — WPF y lit « toutes ». Il ne suffit pas pour les
    /// COLLECTIONS : une liste déjà remplie ne se vide pas parce qu'on la déclare périmée.
    /// Le catalogue anglais était bien chargé — le journal l'annonçait — mais l'onglet des
    /// commandes continuait d'afficher les noms français, faute d'avoir été repeuplé.
    /// </summary>
    /// <summary>
    /// Rejoue tout ce que la langue touche.
    ///
    /// <c>Raise(null)</c> dit à WPF que « toutes les propriétés ont changé » et suffit aux
    /// libellés. Il ne repeuple <b>aucune</b> ObservableCollection : leurs éléments sont des
    /// objets déjà construits, portant des noms lus dans le catalogue de l'ancienne langue.
    ///
    /// Chaque liste oubliée ici reste donc affichée dans la langue précédente jusqu'au
    /// redémarrage. Les commandes et les touches l'ont été le 2026-08-28, les macros et les
    /// hésitations le 2026-08-29 — signalées par le pilote, qui voyait « Procédure
    /// d'atterrissage » dans un écran par ailleurs anglais.
    ///
    /// Toute nouvelle liste tirée du catalogue doit être ajoutée ici. C'est la seule
    /// précaution qui tienne : rien dans WPF ne signalera l'oubli.
    /// </summary>
    private void RefreshLanguage() => _dispatcher.BeginInvoke(() =>
    {
        Raise(null);
        RefreshCommands();
        RefreshBindings();
        MacroEditor.Refresh();
        Understanding.Refresh();
    });

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(ListeningLabel));
            Raise(nameof(IsListening));
            Raise(nameof(ModeLabel));
            Raise(nameof(IsReal));
            Raise(nameof(KillSwitch));
            Raise(nameof(KillSwitchLabel));
            Raise(nameof(CombatActive));
            Raise(nameof(BindingSummary));
            Raise(nameof(WakeWord));
            Raise(nameof(WakeHint));
            Raise(nameof(VoiceName));
            Raise(nameof(CopilotName));

            // Le profil de touches peut avoir change sans passer par cet ecran : une bascule
            // demandee a la voix vient du moteur, pas de la liste deroulante. Sans ce
            // rafraichissement, l'onglet afficherait encore l'ancien profil et ses anciennes
            // touches - un ecran qui ment sur ce qu'Optimus va reellement envoyer.
            RefreshProfiles();
            RefreshBindings();
        });
    }

    private void Append(SessionActivity activity)
    {
        VoiceRecognition? heard = activity.Recognition;

        if (heard is not null && heard.Outcome == RecognitionOutcome.Noise)
        {
            // Bruit ambiant : le cas de loin le plus frequent en ecoute permanente. On le note
            // discretement plutot que de le taire - c'est en le voyant qu'on calibre les seuils.
            if (!string.IsNullOrWhiteSpace(heard.Text))
            {
                Add(Localization.Localizer.T("Log.Ignored", heard.Text), Localization.Localizer.T("Log.Confidence", heard.Confidence.ToString("F2")), ActivityLevel.Muted);
            }

            return;
        }

        if (heard is not null)
        {
            Add(Localization.Localizer.T("Log.Heard", heard.Text),
                Localization.Localizer.T("Log.Confidence", heard.Confidence.ToString("F2"))
                + (heard.Outcome == RecognitionOutcome.Unclear ? Localization.Localizer.T("Log.CalledNotUnderstood") : string.Empty),
                heard.Outcome == RecognitionOutcome.Unclear ? ActivityLevel.Warning : ActivityLevel.Normal);
        }

        if (activity.Result is ExecutionResult result)
        {
            Add(Describe(result), Detail(result), Level(result));
        }

        // Une hesitation qui vient d'arriver doit apparaitre dans l'onglet sans relancer
        // l'application : c'est en vol qu'on les accumule, et apres coup qu'on les traite.
        if (activity.Result?.Status is ExecutionStatus.Unknown or ExecutionStatus.NeedsClarification
            || activity.Recognition?.Outcome == RecognitionOutcome.Unclear)
        {
            Understanding.Refresh();
        }

        if (activity.Spoken is string spoken)
        {
            string? rules = activity.AppliedRules.Count == 0
                ? null
                : Localization.Localizer.T("Log.Rules", string.Join(", ", activity.AppliedRules));

            Add(Localization.Localizer.T("Log.OptimusSaid", spoken), rules, ActivityLevel.Speech);
        }
    }

    private static string Describe(ExecutionResult result) => result.Status switch
    {
        ExecutionStatus.Executed => Localization.Localizer.T("Log.Executed", result.Command?.Name ?? "?"),
        ExecutionStatus.Simulated => Localization.Localizer.T("Log.SimulatedRun", result.Command?.Name ?? "?"),
        ExecutionStatus.Answered => Localization.Localizer.T("Log.Answered", result.Command?.Name ?? "?"),
        ExecutionStatus.NoChangeNeeded => Localization.Localizer.T("Log.NoChange", result.Command?.Name ?? "?"),
        ExecutionStatus.Rejected => Localization.Localizer.T("Log.Rejected", result.Command?.Name ?? "?"),
        ExecutionStatus.NeedsClarification => Localization.Localizer.T("Log.Ambiguous"),
        ExecutionStatus.Unknown => Localization.Localizer.T("Log.NotUnderstood"),
        _ => Localization.Localizer.T("Log.Failed"),
    };

    private static string? Detail(ExecutionResult result)
    {
        List<string> parts = new();

        if (result.Steps.Count > 0)
        {
            // Le ToString() d'un record affiche « SequenceStepTrace { Index = 0, Description =
            // ..., DurationMs = 18,4852 } » : un vidage de debogueur, pas une ligne de journal.
            // Ce qui compte est l'action, la touche et la duree.
            parts.Add(string.Join(" · ", result.Steps.Select(
                step => $"{step.Description} ({step.DurationMs:F1} ms)")));
        }
        else if (result.Message is not null)
        {
            parts.Add(result.Message);
        }

        if (result.Polarity != CommandPolarity.Neutral)
        {
            parts.Add(result.Polarity == CommandPolarity.On ? "activation" : "extinction");
        }

        parts.Add($"{result.TotalMs:F1} ms");

        return string.Join("  ·  ", parts);
    }

    private static ActivityLevel Level(ExecutionResult result) => result.Status switch
    {
        ExecutionStatus.Executed or ExecutionStatus.Simulated or ExecutionStatus.Answered => ActivityLevel.Normal,
        ExecutionStatus.NoChangeNeeded => ActivityLevel.Muted,
        ExecutionStatus.Failed => ActivityLevel.Danger,
        _ => ActivityLevel.Warning,
    };

    private void Add(string title, string? detail, ActivityLevel level) =>
        Add(new ActivityEntry { At = DateTimeOffset.UtcNow, Title = title, Detail = detail, Level = level });

    private void Add(ActivityEntry entry)
    {
        // Ce que l'interface fait - assigner une touche, importer, changer un reglage - doit
        // figurer dans le fichier au meme titre que ce que le moteur fait, sans quoi la trace
        // precedant une chute serait borgne.
        if (entry.Level is ActivityLevel.Warning or ActivityLevel.Danger)
        {
            DiagnosticLog.Warn(entry.Title, entry.Detail);
        }
        else if (entry.Level != ActivityLevel.Muted)
        {
            DiagnosticLog.Info(entry.Title, entry.Detail);
        }

        Journal.Add(entry);

        while (Journal.Count > JournalCapacity)
        {
            Journal.RemoveAt(0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _detectorTimer.Stop();

        _runtime.Activity -= OnActivity;
        _runtime.MicrophoneOpen -= OnMicrophone;
        _runtime.StateChanged -= OnStateChanged;

        await _runtime.DisposeAsync().ConfigureAwait(false);
    }
}
