using Optimus.Core.Abstractions;
using Optimus.Core.Ai;
using Optimus.Infrastructure.Ai;
using Optimus.Core.Bindings;
using Optimus.Core.Diagnostics;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Domain.Profiles;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Core.Loading;
using Optimus.Core.Personality;
using Optimus.Infrastructure.Game;
using Optimus.Infrastructure.Input;
using Optimus.Infrastructure.Speech;

namespace Optimus.Infrastructure.Hosting;

/// <summary>Ce qui s'est passé, tel qu'une interface doit pouvoir l'afficher.</summary>
/// <param name="Recognition">Ce qui a été entendu, quand cela vient du micro.</param>
/// <param name="Result">Issue de l'exécution, si une commande a été tentée.</param>
/// <param name="Spoken">Ce qu'Optimus a répondu, éventuellement rien.</param>
/// <param name="AppliedRules">Règles de comportement ayant joué, pour l'explication.</param>
public sealed record SessionActivity(
    VoiceRecognition? Recognition,
    ExecutionResult? Result,
    string? Spoken,
    IReadOnlyList<BehaviorTrigger> AppliedRules);

/// <summary>
/// Tout ce qu'il faut pour faire fonctionner Optimus, assemblé une fois.
///
/// Le banc d'essai en ligne de commande contenait cet assemblage <i>et</i> une machine à états
/// subtile — proposition en attente, confirmation, polarité, mode de vol — melangée à ses
/// affichages. La reprendre telle quelle dans une interface graphique aurait garanti que les
/// deux divergent : un correctif appliqué d'un côté, oublié de l'autre.
///
/// Ici, rien n'écrit sur une console ni ne connaît de fenêtre. Ce qui se passe est signalé par
/// <see cref="Activity"/>, et chaque interface en fait ce qu'elle veut.
/// </summary>
public sealed class OptimusRuntime : IAsyncDisposable
{
    /// <summary>Durée pendant laquelle une commande proposée attend un « confirme ».</summary>
    private static readonly TimeSpan ProposalLifetime = TimeSpan.FromSeconds(12);

    private readonly SemaphoreSlim _processing = new(1, 1);
    private string _overlayPath;

    private ILanguageModel? _model;
    private IInputEngine _engine;
    private CommandExecutor _executor;
    private WindowsGrammarListener? _listener;
    private PushToTalkWatcher? _pushToTalk;

    private CommandDefinition? _pending;
    private string? _pendingUtterance;
    private CommandPolarity _pendingPolarity;
    private DateTimeOffset _pendingUntil = DateTimeOffset.MinValue;
    private int? _lastGamePid;
    private bool _disposed;

    private OptimusRuntime(
        string dataRoot,
        CommandCatalog catalog,
        BindingProfile bindings,
        BindingOverlay overlay,
        string overlayPath,
        UserProfile user,
        Copilot copilot,
        IReadOnlyList<LoadIssue> issues)
    {
        DataRoot = dataRoot;
        Catalog = catalog;
        DefaultBindings = bindings;
        Overlay = overlay;
        _overlayPath = overlayPath;
        User = user;
        Copilot = copilot;
        Issues = issues;

        Bindings = Compose(bindings, overlay);
        Composer = new ResponseComposer(copilot.Personality, copilot.Responses);
        Speech = SpeechFactory.For(copilot);
        Detector = new StarCitizenDetector();
        State = new CopilotState();

        Simulation = new SimulatedInputEngine();
        SimulationMode = user.SimulationMode;

        // L'etage conversationnel n'est monte que s'il est demande. Sans lui, rien ne change et
        // rien ne part sur le reseau : c'est l'exigence §84, et le defaut.
        AiSettings ai = user.Ai ?? AiSettings.Disabled;

        if (ai.Enabled)
        {
            _model = new HttpLanguageModel(ai);
            Conversation = new ConversationTier(_model, ai);
        }

        _engine = SimulationMode ? Simulation : new SendInputEngine();
        _executor = BuildExecutor();
    }

    /// <summary>Une activité vient d'avoir lieu : entendue, exécutée, ou simplement dite.</summary>
    public event EventHandler<SessionActivity>? Activity;

    /// <summary>Le micro s'ouvre ou se ferme, en push-to-talk.</summary>
    public event EventHandler<bool>? MicrophoneOpen;

    /// <summary>Un réglage observable a changé : écoute, simulation, arrêt d'urgence, mode de vol.</summary>
    public event EventHandler? StateChanged;

    public string DataRoot { get; }

    /// <summary>Fichier du profil utilisateur : réglages d'écoute.</summary>
    public string ProfilePath => Path.Combine(DataRoot, "data", "profiles", "default.json");

    /// <summary>Dossier du copilote : fiche, personnalité, répliques.</summary>
    public string CopilotDirectory =>
        Path.Combine(DataRoot, "data", "copilots", User.PreferredCopilot);

    /// <summary>Fiche du copilote : voix et mot d'éveil.</summary>
    public string CopilotPath => Path.Combine(CopilotDirectory, "copilot.json");

    /// <summary>Fichier de personnalité : curseurs de caractère.</summary>
    public string PersonalityPath => Path.Combine(CopilotDirectory, "personality.json");

    public CommandCatalog Catalog { get; private set; }

    /// <summary>Catalogue livré seul, sans les macros du pilote. Sert à savoir ce qui lui appartient.</summary>
    public CommandCatalog ShippedCatalog { get; private init; } = CommandCatalog.Empty;

    /// <summary>Fichier des macros du pilote.</summary>
    public string MacroPath { get; private init; } = string.Empty;

    /// <summary>Fichier des formulations ajoutées par le pilote.</summary>
    public string PhrasePath { get; private init; } = string.Empty;

    /// <summary>Formulations ajoutées, telles qu'elles sont sur disque.</summary>
    public IReadOnlyList<PhraseAlias> Aliases { get; private set; } = [];

    /// <summary>Ce qu'Optimus a entendu sans agir.</summary>
    public UnderstandingLog Understanding { get; private init; } = new();

    /// <summary>
    /// L'étage conversationnel, ou <c>null</c> s'il n'est pas demandé.
    ///
    /// Nul dans le cas nominal, et c'est voulu : tout le reste doit continuer de fonctionner
    /// sans lui, hors ligne, sans le moindre appel réseau (§84).
    /// </summary>
    public ConversationTier? Conversation { get; private set; }

    /// <summary>Vrai si l'étage conversationnel est monté.</summary>
    public bool HasConversation => Conversation is not null;

    /// <summary>Profil du jeu seul, sans les choix du pilote. Sert à savoir ce qui manque.</summary>
    public BindingProfile DefaultBindings { get; }

    /// <summary>Profil effectif : défauts du jeu ⊕ assignations du pilote.</summary>
    public BindingProfile Bindings { get; private set; }

    public BindingOverlay Overlay { get; private set; }

    /// <summary>Nom du profil de touches en vigueur.</summary>
    public string BindingProfileName { get; private set; } = BindingProfileSet.DefaultName;

    /// <summary>Profils installés, relus à chaque appel : le pilote peut en déposer un à la main.</summary>
    public IReadOnlyList<BindingProfileInfo> BindingProfiles => BindingProfileSet.List();

    public UserProfile User { get; private set; }

    public Copilot Copilot { get; private set; }

    public ResponseComposer Composer { get; private set; }

    public ITextToSpeechProvider Speech { get; private set; }

    /// <summary>Vrai si une installation de Piper est utilisable sur cette machine.</summary>
    public static bool PiperAvailable => PiperInstallation.Locate() is not null;

    public StarCitizenDetector Detector { get; }

    public CopilotState State { get; }

    /// <summary>Moteur simulé, toujours présent : c'est lui qui trace ce qui aurait été envoyé.</summary>
    public SimulatedInputEngine Simulation { get; }

    /// <summary>Anomalies rencontrées au chargement des données. Jamais tues.</summary>
    public IReadOnlyList<LoadIssue> Issues { get; }

    /// <summary>Moteur de reconnaissance en service, ou <c>null</c> tant qu'on n'écoute pas.</summary>
    public string? RecognizerName => _listener?.RecognizerName;

    /// <summary>Nombre d'alternatives de la grammaire chargée.</summary>
    public int GrammarSize { get; private set; }

    public bool IsListening => _listener?.IsListening ?? false;

    /// <summary>
    /// Mode simulation : on trace, on n'appuie pas.
    ///
    /// Lu dans le profil utilisateur (<c>safety.simulation_mode</c>) plutôt que fixé ici : le
    /// mode reste disponible — §56 l'exige, et c'est la seule façon d'essayer une macro sans
    /// conséquence — mais c'est au pilote de décider comment Optimus démarre chez lui.
    /// </summary>
    public bool SimulationMode { get; private set; }

    /// <summary>
    /// Arrêt d'urgence (§37). Tant qu'il est engagé, plus rien ne sort — la garde le refuse
    /// avant même de regarder le reste.
    /// </summary>
    public bool KillSwitch { get; private set; }

    /// <summary>Assemble Optimus à partir d'un dossier de données.</summary>
    public static OptimusRuntime Load(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        LoadResult<CommandCatalog> catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(dataRoot, "data", "commands", "starcitizen.core.json"));

        // Les macros ecrites par le pilote se superposent au catalogue livre : celui-ci est
        // remplace a chaque publication, celles-la doivent survivre.
        string macroPath = UserMacros.DefaultPath();
        LoadResult<CommandCatalog> userMacros = UserMacros.Load(macroPath);

        CommandCatalog merged = userMacros.Value.Count == 0
            ? catalog.Value
            : CommandCatalog.Merge(
                catalog.Value.Id, catalog.Value.Name, catalog.Value, userMacros.Value);

        // Chaque profil de touches apporte sa commande de bascule : « profil minage » doit etre
        // prononcable, sinon changer de style de vol imposerait de quitter le jeu pour cliquer.
        merged = BindingProfileSet.Augment(merged);

        // Les formulations ajoutees par le pilote s'appliquent par-dessus : c'est ce qui rend
        // le reglage de la reconnaissance cumulatif d'une session a l'autre.
        string phrasePath = UserPhrases.DefaultPath();
        IReadOnlyList<PhraseAlias> aliases = UserPhrases.Load(phrasePath);
        merged = UserPhrases.Apply(merged, aliases);
        LoadResult<BindingProfile> bindings = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(dataRoot, "data", "bindings", "starcitizen", "defaults-4.9.json"));
        LoadResult<UserProfile> user = ProfileLoader.Load(
            Path.Combine(dataRoot, "data", "profiles", "default.json"));
        LoadResult<Copilot> copilot = CopilotLoader.Load(
            Path.Combine(dataRoot, "data", "copilots", user.Value.PreferredCopilot));

        // Le profil enregistre s'il existe encore, le premier venu sinon. Un pilote qui
        // supprime a la main le profil actif doit retrouver Optimus fonctionnel, pas un ecran
        // des touches qui enregistre dans un fichier fantome.
        string profileName = BindingProfileSet.Resolve(user.Value.ActiveBindingProfile);
        string overlayPath = BindingProfileSet.PathOf(profileName);

        return new OptimusRuntime(
            dataRoot,
            merged,
            bindings.Value,
            BindingOverlay.Load(overlayPath),
            overlayPath,
            user.Value,
            copilot.Value,
            [.. catalog.Issues, .. userMacros.Issues, .. bindings.Issues, .. user.Issues,
             .. copilot.Issues])
        {
            MacroPath = macroPath,
            PhrasePath = phrasePath,
            ShippedCatalog = catalog.Value,
            Aliases = aliases,
            Understanding = UnderstandingLog.Load(UnderstandingLog.DefaultPath()),
            BindingProfileName = profileName,
        };
    }

    /// <summary>
    /// Remonte jusqu'au dossier contenant <c>data/</c>.
    ///
    /// Cherché plutôt que fixé, parce que l'exécutable vit tantôt dans le dépôt, tantôt dans un
    /// dossier publié copié à la main sur une autre machine.
    /// </summary>
    public static string? FindDataRoot(string startingAt)
    {
        DirectoryInfo? directory = new(startingAt);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "commands")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Bascule entre simulation et envoi réel. Recrée le moteur d'entrée.</summary>
    public void SetSimulation(bool simulation)
    {
        if (SimulationMode == simulation)
        {
            return;
        }

        SimulationMode = simulation;

        if (!ReferenceEquals(_engine, Simulation))
        {
            _engine.Dispose();
        }

        _engine = simulation ? Simulation : new SendInputEngine();
        _executor = BuildExecutor();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Engage ou relâche l'arrêt d'urgence.</summary>
    public void SetKillSwitch(bool engaged)
    {
        if (KillSwitch == engaged)
        {
            return;
        }

        KillSwitch = engaged;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recompose le profil après une modification des assignations.</summary>
    public void ReloadBindings()
    {
        Bindings = Compose(DefaultBindings, Overlay);
        _executor = BuildExecutor();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Bascule vers un autre profil de touches, sans redémarrer.
    ///
    /// Le profil courant est enregistré d'abord : basculer ne doit jamais perdre une assignation
    /// que le pilote vient de poser. Puis la composition et l'exécuteur sont refaits — c'est
    /// tout ce qui dépend des touches, la grammaire n'en dépendant pas, les formulations étant
    /// les mêmes quel que soit le raccourci derrière.
    ///
    /// Le choix est enregistré dans le profil utilisateur : basculer en vol, c'est aussi dire
    /// par quoi on veut recommencer demain.
    /// </summary>
    public void SwitchBindingProfile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string clean = BindingProfileSet.Sanitize(name);

        if (string.Equals(clean, BindingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveOverlay();

        string path = BindingProfileSet.PathOf(clean);

        _overlayPath = path;
        Overlay = BindingOverlay.Load(path);
        BindingProfileName = clean;

        ReloadBindings();

        try
        {
            SettingsWriter.SaveActiveBindingProfile(ProfilePath, clean);
        }
        catch (Exception exception)
        {
            // Le profil est bel et bien actif : ne pas pouvoir s'en souvenir est genant, pas
            // bloquant. Le dire plutot que de defaire une bascule qui a reussi.
            DiagnosticLog.Warn("profil de touches non mémorisé", exception.Message);
        }

        DiagnosticLog.Info(
            $"profil de touches « {clean} »",
            $"{Overlay.Count} assignations · {Bindings.BoundCount} actions liées");
    }

    /// <summary>
    /// Relit les réglages depuis le disque et reconstruit ce qui en dépend.
    ///
    /// Les fichiers restent la source de vérité : l'interface écrit, puis demande une relecture.
    /// Un seul chemin de chargement, donc aucune divergence possible entre ce qui est affiché et
    /// ce qui sera lu au prochain démarrage.
    ///
    /// L'écoute redémarre si elle courait : la grammaire dépend du mot d'éveil et du mode, les
    /// seuils du moteur de reconnaissance. Les garder tièdes donnerait une fenêtre qui affiche
    /// un réglage et un micro qui en applique un autre.
    /// </summary>
    public async Task ReloadSettingsAsync(CancellationToken cancellationToken = default)
    {
        bool wasListening = IsListening;

        if (wasListening)
        {
            await StopListeningAsync().ConfigureAwait(false);
        }

        User = ProfileLoader.Load(ProfilePath).Value;

        Copilot previous = Copilot;
        Copilot = CopilotLoader.Load(CopilotDirectory).Value;
        Composer = new ResponseComposer(Copilot.Personality, Copilot.Responses);

        // Changer de moteur demande d'en construire un autre : le precedent tient un processus
        // Piper ouvert et un lecteur audio, qu'il faut relacher plutot que d'abandonner.
        if (!string.Equals(previous.Voice.Provider, Copilot.Voice.Provider, StringComparison.OrdinalIgnoreCase))
        {
            ITextToSpeechProvider stale = Speech;
            Speech = SpeechFactory.For(Copilot);

            await stale.DisposeAsync().ConfigureAwait(false);
            await Speech.WarmUpAsync(Copilot.Voice.VoiceId, cancellationToken).ConfigureAwait(false);
        }

        if (wasListening)
        {
            await StartListeningAsync(cancellationToken).ConfigureAwait(false);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Relit les macros du pilote et reconstruit ce qui en dépend.
    ///
    /// La grammaire en fait partie : une macro nouvelle apporte ses formulations, et l'écoute
    /// doit repartir pour que le moteur les connaisse. Les garder tièdes donnerait une macro
    /// visible dans la fenêtre mais inaudible au micro.
    /// </summary>
    public async Task ReloadMacrosAsync(CancellationToken cancellationToken = default)
    {
        bool wasListening = IsListening;

        if (wasListening)
        {
            await StopListeningAsync().ConfigureAwait(false);
        }

        LoadResult<CommandCatalog> userMacros = UserMacros.Load(MacroPath);

        CommandCatalog rebuilt = userMacros.Value.Count == 0
            ? ShippedCatalog
            : CommandCatalog.Merge(
                ShippedCatalog.Id, ShippedCatalog.Name, ShippedCatalog, userMacros.Value);

        Catalog = UserPhrases.Apply(BindingProfileSet.Augment(rebuilt), Aliases);

        _executor = BuildExecutor();

        if (wasListening)
        {
            await StartListeningAsync(cancellationToken).ConfigureAwait(false);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enregistre les formulations du pilote, puis reconstruit le catalogue et la grammaire.
    ///
    /// L'écoute repart : c'est elle qui porte la grammaire, et une formulation qu'Optimus
    /// connaîtrait sans l'entendre ne servirait à rien.
    /// </summary>
    public async Task SaveAliasesAsync(
        IReadOnlyList<PhraseAlias> aliases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        UserPhrases.Save(PhrasePath, aliases);
        Aliases = aliases;

        await ReloadMacrosAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Enregistre le journal de compréhension sur disque.</summary>
    public void SaveUnderstanding() => Understanding.Save(UnderstandingLog.DefaultPath());

    /// <summary>Enregistre les assignations sur disque.</summary>
    public void SaveOverlay() => Overlay.Save(_overlayPath);

    /// <summary>Chemin du fichier d'assignations.</summary>
    public string OverlayPath => _overlayPath;

    /// <summary>Démarre l'écoute du micro.</summary>
    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listener is not null)
        {
            return;
        }

        VoiceInputSettings settings = User.VoiceInput;
        VoiceGrammar grammar = VoiceGrammarBuilder.Build(Catalog, Copilot.WakeWord, settings);
        GrammarSize = grammar.Count;

        DiagnosticLog.Info(
            "démarrage de l'écoute",
            $"mode {settings.Mode} · {grammar.Count} alternatives · mot d'éveil « {Copilot.WakeWord} » · "
            + $"seuils bruit {settings.NoiseFloor:F2} / exécution {settings.ConfidenceThreshold:F2} · "
            + $"langue {Copilot.Language}");

        try
        {
            _listener = new WindowsGrammarListener(
                grammar, settings.ConfidenceThreshold, settings.NoiseFloor, Copilot.Language);
        }
        catch (Exception exception)
        {
            // Cause la plus frequente : aucun peripherique d'entree, ou pas de moteur de
            // reconnaissance installe pour cette langue. Le dire vaut mieux que de tomber.
            DiagnosticLog.Error(
                "impossible d'ouvrir le moteur de reconnaissance", exception);
            throw new InvalidOperationException(
                "Le moteur de reconnaissance vocale n'a pas pu démarrer. Vérifiez qu'un microphone "
                + "est branché et que la reconnaissance vocale Windows est installée pour le français.",
                exception);
        }

        _listener.Recognized += OnRecognized;

        if (settings.Mode == ListeningMode.PushToTalk)
        {
            // Hors appui, la grammaire est desactivee : le moteur n'a plus rien a reconnaitre.
            _listener.SetActive(false);
            _pushToTalk = new PushToTalkWatcher(settings.PushToTalkKey);
            _pushToTalk.StateChanged += OnPushToTalk;
            _pushToTalk.Start();
        }

        await _listener.StartAsync(cancellationToken).ConfigureAwait(false);

        DiagnosticLog.Info("écoute active", _listener.RecognizerName);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Arrête l'écoute et libère le micro.</summary>
    public async Task StopListeningAsync()
    {
        if (_pushToTalk is not null)
        {
            _pushToTalk.StateChanged -= OnPushToTalk;
            _pushToTalk.Dispose();
            _pushToTalk = null;
        }

        if (_listener is not null)
        {
            _listener.Recognized -= OnRecognized;
            await _listener.DisposeAsync().ConfigureAwait(false);
            _listener = null;

            DiagnosticLog.Info("écoute arrêtée");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Préchauffe la synthèse : la première phrase coûte jusqu'à 429 ms sinon (D23).</summary>
    public Task WarmUpAsync() => Speech.WarmUpAsync(Copilot.Voice.VoiceId);

    /// <summary>
    /// Traite un énoncé écrit, comme s'il avait été entendu. Sert au banc d'essai et au champ
    /// de test de l'interface.
    /// </summary>
    public Task HandleUtteranceAsync(string utterance, CancellationToken cancellationToken = default) =>
        ExecuteAsync(utterance, null, CommandPolarity.Neutral, cancellationToken);

    /// <summary>Exécute une commande désignée, sans passer par la reconnaissance.</summary>
    public async Task<ExecutionResult> RunCommandAsync(
        CommandDefinition command,
        CommandPolarity polarity = CommandPolarity.Neutral,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Simulation.Reset();
        ExecutionEnvironment environment = Environment();

        ExecutionResult result = await _executor
            .ExecuteCommandAsync(command, environment, Timing(), polarity: polarity,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await AfterExecutionAsync(result, null, null).ConfigureAwait(false);
        return result;
    }

    private void OnPushToTalk(object? sender, bool pressed)
    {
        _listener?.SetActive(pressed);
        MicrophoneOpen?.Invoke(this, pressed);
    }

    private async void OnRecognized(object? sender, VoiceRecognition recognition)
    {
        // Une seule commande traitee a la fois : deux sequences d'entrees qui se chevaucheraient
        // enverraient des touches entremelees au jeu.
        if (!await _processing.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await HandleRecognitionAsync(recognition).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Une exception dans un gestionnaire « async void » abattrait le processus. Optimus
            // doit survivre a une commande ratee : on la trace, on la signale, on continue.
            DiagnosticLog.Error($"échec du traitement de « {recognition.Text} »", exception);
            Report(new SessionActivity(recognition, null, $"Erreur interne : {exception.Message}", []));
        }
        finally
        {
            _processing.Release();
        }
    }

    private async Task HandleRecognitionAsync(VoiceRecognition recognition)
    {
        switch (recognition.Outcome)
        {
            case RecognitionOutcome.Noise:
                // Bruit ambiant ou parole hors grammaire : le cas de loin le plus frequent en
                // ecoute permanente, et le seul ou se taire est la bonne reponse.
                Report(new SessionActivity(recognition, null, null, []));
                return;

            case RecognitionOutcome.Unclear:
                // Interpelle sans etre compris : le signal le plus utile pour ajuster les
                // formulations, et le seul dont on dispose avec une grammaire fermee.
                Understanding.Record(
                    recognition.Text, HesitationKind.Proposed,
                    recognition.CommandId, recognition.Confidence);

                await ProposeAsync(recognition).ConfigureAwait(false);
                return;

            case RecognitionOutcome.Accepted:
            default:
                await AcceptAsync(recognition).ConfigureAwait(false);
                return;
        }
    }

    private async Task ProposeAsync(VoiceRecognition recognition)
    {
        // Une commande a bien ete reconnue, sans assez de certitude pour agir. On la propose :
        // refuser une commande valide est aussi penible qu'executer celle qu'on n'a pas demandee.
        if (recognition.CommandId is not null &&
            Catalog.TryGet(recognition.CommandId, out CommandDefinition? candidate) &&
            candidate is not null)
        {
            _pending = candidate;
            _pendingUtterance = recognition.Text;
            _pendingPolarity = recognition.Polarity;
            _pendingUntil = DateTimeOffset.UtcNow + ProposalLifetime;

            string? said = await SayAsync(
                ["system.propose"], ResponseEvent.Clarify,
                new Dictionary<string, string> { ["command"] = candidate.Name }).ConfigureAwait(false);

            Report(new SessionActivity(recognition, null, said, []));
            return;
        }

        string? unknown = await SayAsync(["system.unknown_command"], ResponseEvent.Unknown)
            .ConfigureAwait(false);

        Report(new SessionActivity(recognition, null, unknown, []));
    }

    private async Task AcceptAsync(VoiceRecognition recognition)
    {
        bool pendingAlive = _pending is not null && DateTimeOffset.UtcNow <= _pendingUntil;

        if (recognition.CommandId == "system.confirm" && pendingAlive)
        {
            CommandDefinition confirmed = _pending!;
            string? utterance = _pendingUtterance;
            CommandPolarity polarity = _pendingPolarity;
            ClearProposal();

            Simulation.Reset();

            ExecutionResult result = await _executor
                .ExecuteCommandAsync(confirmed, Environment(), Timing(), polarity: polarity)
                .ConfigureAwait(false);

            await AfterExecutionAsync(result, recognition, utterance).ConfigureAwait(false);
            return;
        }

        if (recognition.CommandId == "system.deny" && pendingAlive)
        {
            // Une proposition refusee dit plus qu'une hesitation : le rattachement etait faux.
            if (_pending is CommandDefinition refused)
            {
                Understanding.Record(
                    _pendingUtterance ?? refused.Name, HesitationKind.Denied,
                    refused.Id, recognition.Confidence);
            }

            ClearProposal();
            string? said = await SayAsync(["system.deny"], ResponseEvent.Any).ConfigureAwait(false);
            Report(new SessionActivity(recognition, null, said, []));
            return;
        }

        // Toute autre commande annule la proposition en attente : le pilote est passe a autre chose.
        ClearProposal();

        await ExecuteAsync(recognition.Text, recognition, recognition.Polarity, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task ExecuteAsync(
        string utterance,
        VoiceRecognition? recognition,
        CommandPolarity polarity,
        CancellationToken cancellationToken)
    {
        Simulation.Reset();

        ExecutionResult result = await _executor
            .ExecuteUtteranceAsync(
                utterance, Environment(), Copilot.WakeWord, Timing(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await AfterExecutionAsync(result, recognition, result.Intent?.NormalizedText).ConfigureAwait(false);
    }

    private async Task AfterExecutionAsync(
        ExecutionResult result, VoiceRecognition? recognition, string? utterance)
    {
        State.Record(result);

        // Bascule de profil de touches demandee a la voix. Comme le mode de vol, elle se fait
        // ici plutot que dans l'executeur : c'est un etat d'Optimus, pas une touche envoyee au
        // jeu, et la commande est passive pour cette raison.
        if (BindingProfileSet.ProfileOf(result.Command) is string profile && result.Succeeded)
        {
            try
            {
                SwitchBindingProfile(profile);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Warn($"bascule vers « {profile} » impossible", exception.Message);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // Bascule declarative du mode de vol : faute de telemetrie, Optimus se fie a ce que le
        // pilote lui annonce, et lit le sens dans la phrase plutot que de basculer a l'aveugle.
        if (result.Command?.Id == MasterMode.CommandId && result.Succeeded)
        {
            State.ApplyMasterMode(result.Polarity, utterance);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // Le chemin rapide n'a rien su faire de cet enonce. C'est ICI, et pas avant, que
        // l'etage conversationnel intervient : le chemin rapide est deterministe, teste et
        // instantane, la ou un modele est lent, variable et faillible. Lui donner la main plus
        // tot echangerait de la fiabilite contre de la souplesse.
        if (result.Status == ExecutionStatus.Unknown
            && Conversation is not null
            && result.Intent?.RawText is string spoken)
        {
            ExecutionResult? escalated = await EscalateAsync(spoken, recognition).ConfigureAwait(false);

            if (escalated is not null)
            {
                return;
            }
        }

        // Non compris ou ambigu : on le note pour que le pilote puisse y attacher sa tournure.
        if (result.Status is ExecutionStatus.Unknown or ExecutionStatus.NeedsClarification)
        {
            Understanding.Record(
                result.Intent?.RawText ?? utterance ?? "?",
                result.Status == ExecutionStatus.Unknown
                    ? HesitationKind.Unknown
                    : HesitationKind.Ambiguous,
                result.Command?.Id,
                result.Intent?.Best?.Score ?? 0);
        }

        ResponseRequest? request = ResponseRouter.Route(result, State.Snapshot());

        string? said = null;
        IReadOnlyList<BehaviorTrigger> applied = [];

        if (request is not null)
        {
            EffectiveBehavior behavior = BehaviorEngine.Resolve(
                Copilot.Personality.Rules, State.Snapshot(), request.Event);

            applied = behavior.AppliedRules;
            said = await SayAsync(request.Keys, request.Event, request.Variables, behavior)
                .ConfigureAwait(false);
        }

        Report(new SessionActivity(recognition, result, said, applied));
    }

    /// <summary>
    /// Demande au modèle ce qu'il faut faire d'un énoncé incompris.
    ///
    /// Trois issues, et une seule mène à une exécution : une commande <b>du catalogue</b>, qui
    /// repart alors par le chemin normal — même garde, même temporisation, même confirmation
    /// pour ce qui est dangereux. Le modèle n'a pas de voie réservée.
    ///
    /// Retourne <c>null</c> si rien n'a pu être tiré de la proposition, auquel cas l'appelant
    /// reprend son cours habituel.
    /// </summary>
    private async Task<ExecutionResult?> EscalateAsync(
        string utterance, VoiceRecognition? recognition)
    {
        AiOutcome outcome;

        try
        {
            outcome = await Conversation!
                .ResolveAsync(utterance, Catalog, Copilot)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Un etage facultatif ne fait pas tomber le reste.
            DiagnosticLog.Warn("étage conversationnel indisponible", exception.Message);
            return null;
        }

        DiagnosticLog.Info(
            $"modèle · {outcome.Decision.Kind}",
            $"{outcome.ElapsedMs:F0} ms · {Conversation.ModelId} · {outcome.Decision.Reasoning}");

        switch (outcome.Decision.Kind)
        {
            case AiDecisionKind.Command when outcome.Decision.CommandId is string id
                && Catalog.TryGet(id, out CommandDefinition? command) && command is not null:
                {
                    // Repasse par l'execution ordinaire : la garde s'applique, la temporisation
                    // aussi, et une commande dangereuse demandera sa confirmation.
                    ExecutionResult result = await RunCommandAsync(
                        command, outcome.Decision.Polarity).ConfigureAwait(false);

                    return result;
                }

            case AiDecisionKind.Conversation when outcome.Decision.Reply is string reply:
                {
                    await SpeakAsync(reply).ConfigureAwait(false);
                    Report(new SessionActivity(recognition, null, reply, []));
                    return null;
                }

            case AiDecisionKind.Clarification when outcome.Decision.Question is string question:
                {
                    await SpeakAsync(question).ConfigureAwait(false);
                    Report(new SessionActivity(recognition, null, question, []));
                    return null;
                }

            default:
                return null;
        }
    }

    /// <summary>Prononce un texte libre, sans passer par le catalogue de répliques.</summary>
    private async Task SpeakAsync(string text)
    {
        try
        {
            await Speech.SpeakAsync(new SpeechRequest(
                text, Copilot.Voice.VoiceId, Copilot.EffectiveRate, Copilot.Voice.Volume))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("synthèse impossible", exception);
        }
    }

    /// <summary>
    /// Compose et prononce. La parole vient <b>après</b> l'action, jamais avant : une synthèse
    /// lente doit dégrader le confort, jamais la réactivité du jeu (docs/09).
    /// </summary>
    private async Task<string?> SayAsync(
        IReadOnlyList<string> keys,
        ResponseEvent responseEvent,
        IReadOnlyDictionary<string, string>? variables = null,
        EffectiveBehavior? behavior = null)
    {
        behavior ??= BehaviorEngine.Resolve(Copilot.Personality.Rules, State.Snapshot(), responseEvent);

        IReadOnlyList<string> allKeys = behavior.PreferredKeys.Count == 0
            ? keys
            : [.. behavior.PreferredKeys, .. keys];

        ComposedResponse? composed = Composer.ComposeFirst(
            allKeys, responseEvent, variables,
            ResponseContext.From(behavior, State.CombatActive));

        if (composed is null)
        {
            return null;
        }

        await Speech.SpeakAsync(new SpeechRequest(
            composed.Text, Copilot.Voice.VoiceId, Copilot.EffectiveRate, Copilot.Voice.Volume))
            .ConfigureAwait(false);
        return composed.Text;
    }

    private ExecutionEnvironment Environment()
    {
        // L'environnement est re-observe a chaque enonce : le jeu a pu perdre le focus entre
        // deux commandes, et c'est justement ce que la garde doit voir.
        GameStatus game = SimulationMode ? GameStatus.NotRunning : Detector.Detect();

        ForgetBeliefIfGameRestarted(game);

        // Le mode de vol suit : c'est le seul etat du vaisseau qu'Optimus croie connaitre, et
        // une macro qui branche dessus doit lire ce qu'il croit maintenant, pas au demarrage.
        return SimulationMode
            ? ExecutionEnvironment.Sandbox with
            {
                KillSwitchEngaged = KillSwitch,
                CombatActive = State.CombatActive,
            }
            : new ExecutionEnvironment(
                SimulationMode: false,
                GameRunning: game.IsRunning,
                GameForeground: game.IsForeground,
                RequireGameForeground: true,
                KillSwitchEngaged: KillSwitch,
                CombatActive: State.CombatActive);
    }

    /// <summary>
    /// Reconstruit le catalogue et la grammaire apres un changement de profils.
    ///
    /// L'ecoute repart : c'est elle qui porte la grammaire, et un profil cree dont Optimus
    /// connaitrait le nom sans savoir l'entendre ne servirait a rien.
    /// </summary>
    public Task ReloadBindingProfilesAsync(CancellationToken cancellationToken = default) =>
        ReloadMacrosAsync(cancellationToken);

    private SequenceOptions Timing() =>
        SimulationMode ? SequenceOptions.Instant : new SequenceOptions(RealTime: true);

    private void ForgetBeliefIfGameRestarted(GameStatus game)
    {
        // Un nouveau processus de jeu, c'est un vaisseau reparti d'un etat neuf : ce qu'Optimus
        // croyait savoir des bascules ne vaut plus rien.
        if (!game.IsRunning)
        {
            _lastGamePid = null;
            return;
        }

        if (game.ProcessId != _lastGamePid)
        {
            _executor.Belief.Forget();
            _lastGamePid = game.ProcessId;
        }
    }

    private void ClearProposal()
    {
        _pending = null;
        _pendingUtterance = null;
        _pendingPolarity = CommandPolarity.Neutral;
        _pendingUntil = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Assemble l'exécuteur, narrateur compris.
    ///
    /// Reconstruit à chaque changement de moteur ou de profil : garder un exécuteur qui pointe
    /// vers l'ancien moteur d'entrée enverrait les touches là où plus personne ne regarde.
    /// </summary>
    private CommandExecutor BuildExecutor() => new(
        Catalog, Bindings, _engine, new FastIntentMatcher(Catalog), narrate: NarrateAsync);

    /// <summary>Prononce une étape de macro, sans jamais interrompre la séquence.</summary>
    private async Task NarrateAsync(string responseKey, CancellationToken cancellationToken)
    {
        try
        {
            await SayAsync([responseKey], ResponseEvent.Any).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Une macro qui s'arrete parce qu'une phrase n'a pas pu etre dite serait absurde :
            // le vaisseau resterait a mi-chemin pour un probleme de confort.
            DiagnosticLog.Warn($"narration impossible pour « {responseKey} »", exception.Message);
        }
    }

    private void Report(SessionActivity activity)
    {
        // La trace de ce qui precede une chute vaut souvent mieux que la pile d'appels : elle
        // dit ce qu'Optimus etait en train de faire, et donc quoi reproduire.
        if (activity.Result is ExecutionResult result)
        {
            DiagnosticLog.Info(
                $"{result.Status} · {result.Command?.Id ?? "?"}",
                $"trace {result.TraceId} · {result.TotalMs:F1} ms"
                + (result.Message is null ? string.Empty : $" · {result.Message}"));
        }
        else if (activity.Recognition is VoiceRecognition heard && heard.Outcome != RecognitionOutcome.Noise)
        {
            DiagnosticLog.Debug($"entendu « {heard.Text} »", $"confiance {heard.Confidence:F2}");
        }

        Activity?.Invoke(this, activity);
    }

    private static BindingProfile Compose(BindingProfile defaults, BindingOverlay overlay) =>
        overlay.Count == 0
            ? defaults
            : defaults.WithOverrides(overlay.Assignments.Select(
                a => new Binding(a.ActionId, a.Input)));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopListeningAsync().ConfigureAwait(false);

        try
        {
            SaveUnderstanding();
        }
        catch (Exception exception)
        {
            // Perdre le journal de comprehension est facheux, pas grave : il se reconstruit en
            // volant. Empecher la fermeture pour cela serait absurde.
            DiagnosticLog.Warn("journal de compréhension non enregistré", exception.Message);
        }

        if (_model is not null)
        {
            await _model.DisposeAsync().ConfigureAwait(false);
        }

        await Speech.DisposeAsync().ConfigureAwait(false);

        if (!ReferenceEquals(_engine, Simulation))
        {
            _engine.Dispose();
        }

        Simulation.Dispose();
        _processing.Dispose();
    }
}
