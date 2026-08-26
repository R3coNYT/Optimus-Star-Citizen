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
                return "réponse seule";
            }

            string? actionId = Command.ReferencedActionIds.FirstOrDefault();
            if (actionId is null)
            {
                return "—";
            }

            return bindings.Resolve(actionId, out Binding? binding) switch
            {
                BindingLookup.Bound => binding!.Input.ToString(),
                BindingLookup.NotBound => "à configurer",
                BindingLookup.Unsupported => "non injectable",
                _ => "action inconnue",
            };
        }
    }

    public bool NeedsBinding => Binding == "à configurer";
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

    public MainViewModel(OptimusRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = Dispatcher.CurrentDispatcher;

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
        ExportLayoutCommand = new RelayCommand(ExportLayout);
        TestCommandCommand = new AsyncRelayCommand(TestCommandAsync, () => SelectedCommand is not null);
        Settings = new SettingsViewModel(_runtime, Add);
        MacroEditor = new MacroEditorViewModel(_runtime, Add, ReloadMacrosAsync);

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

    public RelayCommand ExportLayoutCommand { get; }

    public AsyncRelayCommand TestCommandCommand { get; }

    /// <summary>Les réglages, dans leur propre onglet.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>L'éditeur de macros.</summary>
    public MacroEditorViewModel MacroEditor { get; }

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

    public string VoiceName => _runtime.Copilot.Voice.VoiceId ?? "voix par défaut";

    public string WakeWord => _runtime.Copilot.WakeWord;

    public string CatalogSummary =>
        $"{_runtime.Catalog.Count} commandes · {_runtime.Catalog.Commands.Sum(c => c.AllPhrases.Count())} formulations";

    /// <summary>
    /// Compte ce que la liste montre, et rien d'autre.
    ///
    /// Afficher les 627 actions du profil complet à côté d'une liste qui n'en montre que 68
    /// laissait croire à un filtre invisible.
    /// </summary>
    public string BindingSummary =>
        $"{Bindings.Count} actions du catalogue · {Bindings.Count(s => s.IsBound)} liées"
        + (BlockingCount > 0 ? $" · {BlockingCount} bloquantes" : " · aucune bloquante");

    public int BlockingCount => Bindings.Count(s => !s.IsBound && s.Need == ActionNeed.Primary);

    public string ListeningLabel => _runtime.IsListening ? "Arrêter l'écoute" : "Écouter";

    public bool IsListening => _runtime.IsListening;

    public string ModeLabel => _runtime.SimulationMode ? "SIMULATION" : "MODE RÉEL";

    public bool IsReal => !_runtime.SimulationMode;

    public bool KillSwitch => _runtime.KillSwitch;

    public string KillSwitchLabel => _runtime.KillSwitch ? "Débloquer les commandes" : "ARRÊT D'URGENCE";

    public bool CombatActive => _runtime.State.CombatActive;

    public string GameLabel => _game.IsRunning
        ? $"détecté · {(_game.IsForeground ? "au premier plan" : "en arrière-plan")}"
        : "non détecté";

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
        await Settings.LoadVoicesAsync().ConfigureAwait(true);
    }

    private async Task ToggleListeningAsync()
    {
        if (_runtime.IsListening)
        {
            await _runtime.StopListeningAsync().ConfigureAwait(true);
            Add("écoute arrêtée", null, ActivityLevel.Muted);
            return;
        }

        await _runtime.StartListeningAsync().ConfigureAwait(true);

        Add("écoute démarrée",
            $"{_runtime.RecognizerName} · {_runtime.GrammarSize} alternatives · « {WakeWord} » obligatoire",
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
                "Les touches seront réellement envoyées au jeu.\n\n"
                + "Placez-vous dans Star Citizen, vaisseau posé, avant de lancer une commande.\n\n"
                + "Continuer ?",
                "Passer en mode réel",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        _runtime.SetSimulation(!goingReal);
        Add(goingReal ? "mode réel activé" : "retour en simulation", null,
            goingReal ? ActivityLevel.Warning : ActivityLevel.Muted);
    }

    private void ToggleKillSwitch()
    {
        _runtime.SetKillSwitch(!_runtime.KillSwitch);

        Add(_runtime.KillSwitch ? "ARRÊT D'URGENCE engagé" : "commandes débloquées", null,
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
            Add($"pressez la touche pour « {slot.CommandName} »", "Échap pour renoncer", ActivityLevel.Speech);

            InputSpec? captured = await WindowKeyCapture
                .CaptureAsync(Owner, TimeSpan.FromSeconds(20))
                .ConfigureAwait(true);

            if (captured is null)
            {
                Add("capture abandonnée", null, ActivityLevel.Muted);
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
                    ? "N'oubliez pas d'exporter vers le jeu, sinon la frappe partira dans le vide."
                    : $"Attention : cette touche sert déjà à {clash.ActionId}.",
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

        Add($"assignation retirée de « {slot.CommandName} »",
            "Star Citizen garde la touche qu'il a apprise : Optimus ne l'enverra plus, "
            + "mais elle reste active si vous la pressez vous-même.",
            ActivityLevel.Warning);

        RefreshBindings();
        RefreshCommands();
    }

    private void ImportLayout()
    {
        string? mappings = StarCitizenDetector.ResolveMappingsDirectory(_game.ExecutablePath);

        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Profil de commandes exporté depuis Star Citizen",
            Filter = "Profils Star Citizen (layout_*.xml)|layout_*.xml|Tous les fichiers XML|*.xml",
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

        Add($"{adopted} réglage(s) repris de « {import.LayoutName ?? Path.GetFileName(dialog.FileName)} »",
            import.Skipped.Count > 0 ? $"{import.Skipped.Count} écarté(s) : {import.Skipped[0]}" : null,
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
            Add("rien à exporter", "Aucune assignation manuelle : les réglages importés du jeu y sont déjà.",
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

        Add($"{manual.Length} assignation(s) exportée(s)",
            inGameFolder
                ? "Déjà dans le dossier du jeu. Dans Star Citizen : pp_RebindKeys optimus"
                : $"Écrit dans {target} — à copier dans le dossier Mappings du jeu.",
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

    private void OnActivity(object? sender, SessionActivity activity)
    {
        _dispatcher.BeginInvoke(() => Append(activity));
    }

    private void OnMicrophone(object? sender, bool open)
    {
        _dispatcher.BeginInvoke(() => Add(open ? "micro ouvert" : "micro fermé", null, ActivityLevel.Muted));
    }

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
            Raise(nameof(VoiceName));
            Raise(nameof(CopilotName));
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
                Add($"ignoré · {heard.Text}", $"confiance {heard.Confidence:F2}", ActivityLevel.Muted);
            }

            return;
        }

        if (heard is not null)
        {
            Add($"entendu · {heard.Text}",
                $"confiance {heard.Confidence:F2}"
                + (heard.Outcome == RecognitionOutcome.Unclear ? " — interpellé, mais pas compris" : string.Empty),
                heard.Outcome == RecognitionOutcome.Unclear ? ActivityLevel.Warning : ActivityLevel.Normal);
        }

        if (activity.Result is ExecutionResult result)
        {
            Add(Describe(result), Detail(result), Level(result));
        }

        if (activity.Spoken is string spoken)
        {
            string? rules = activity.AppliedRules.Count == 0
                ? null
                : "règles : " + string.Join(", ", activity.AppliedRules);

            Add($"Optimus « {spoken} »", rules, ActivityLevel.Speech);
        }
    }

    private static string Describe(ExecutionResult result) => result.Status switch
    {
        ExecutionStatus.Executed => $"exécuté · {result.Command?.Name}",
        ExecutionStatus.Simulated => $"simulé · {result.Command?.Name}",
        ExecutionStatus.Answered => $"répondu · {result.Command?.Name}",
        ExecutionStatus.NoChangeNeeded => $"rien à changer · {result.Command?.Name}",
        ExecutionStatus.Rejected => $"refusé · {result.Command?.Name}",
        ExecutionStatus.NeedsClarification => "ambigu",
        ExecutionStatus.Unknown => "non compris",
        _ => "échec",
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
