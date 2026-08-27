using Optimus.Core.Abstractions;
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

namespace Optimus.Cli;

/// <summary>
/// Banc d'essai du moteur.
///
/// Par défaut, tout se déroule en simulation : la chaîne complète — énoncé, normalisation,
/// résolution d'intention, garde, binding, séquence — s'exécute sans qu'aucune touche ne parte.
/// Le mode réel existe, mais il faut le demander explicitement avec <c>--real</c>, et il exige
/// que Star Citizen soit lancé et au premier plan.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Sans cela, les accents ressortent en charabia selon la page de codes de la console.
        // Peut échouer si la sortie est redirigée : ce n'est jamais une raison d'abandonner.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Sortie redirigée ou console indisponible.
        }

        // Le banc d'essai journalise dans le meme dossier que l'application, sous son propre
        // nom : les deux peuvent tourner en meme temps sans melanger leurs traces.
        DiagnosticLog.Start("Optimus.Cli");

        bool real = args.Contains("--real", StringComparer.OrdinalIgnoreCase);
        bool status = args.Contains("--status", StringComparer.OrdinalIgnoreCase);
        bool silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        bool listVoices = args.Contains("--voices", StringComparer.OrdinalIgnoreCase);
        bool showBindings = args.Contains("--bindings", StringComparer.OrdinalIgnoreCase);
        bool bindRequested = args.Contains("--bind", StringComparer.OrdinalIgnoreCase);
        string? bindTarget = OptionValue(args, "--bind");
        bool importRequested = args.Contains("--import-layout", StringComparer.OrdinalIgnoreCase);
        string? importLayout = OptionValue(args, "--import-layout");
        bool exportLayout = args.Contains("--export-layout", StringComparer.OrdinalIgnoreCase);
        bool unbindRequested = args.Contains("--unbind", StringComparer.OrdinalIgnoreCase);
        string? unbindTarget = OptionValue(args, "--unbind");
        bool resetBindings = args.Contains("--reset-bindings", StringComparer.OrdinalIgnoreCase);
        string? exportPath = OptionValue(args, "--export-layout");
        string[] rest = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        string? repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Racine du dépôt introuvable (dossier « data » attendu au-dessus de l'exécutable).");
            return 1;
        }

        string catalogPath = Path.Combine(repoRoot, "data", "commands", "starcitizen.core.json");
        string profilePath = Path.Combine(repoRoot, "data", "bindings", "starcitizen", "defaults-4.9.json");

        foreach (string path in new[] { catalogPath, profilePath })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Fichier manquant : {path}");
                return 1;
            }
        }

        LoadResult<CommandCatalog> catalog = JsonCatalogLoader.LoadCatalog(catalogPath);
        LoadResult<BindingProfile> profile = JsonCatalogLoader.LoadBindingProfile(profilePath);

        _userProfilePath = Path.Combine(repoRoot, "data", "profiles", "default.json");

        LoadResult<UserProfile> user = ProfileLoader.Load(_userProfilePath);

        // Les touches choisies par le pilote se posent par-dessus le profil du jeu, qui reste
        // intact et donc remplacable a chaque mise a jour (« defauts + deltas »).
        //
        // Le PROFIL ACTIF, et non un chemin fixe : le banc et l'application doivent lire et
        // ecrire le meme fichier, sans quoi « --bind » modifierait un profil qu'Optimus ne
        // regarde plus, et le pilote verrait sa touche disparaitre sans explication.
        string bindingProfile = BindingProfileSet.Resolve(user.Value.ActiveBindingProfile);
        string overlayPath = BindingProfileSet.PathOf(bindingProfile);
        BindingOverlay overlay = BindingOverlay.Load(overlayPath);

        if (overlay.Count > 0)
        {
            profile = profile with { Value = profile.Value.WithOverrides(BindingEditor.ToBindings(overlay)) };
        }

        // Le banc doit connaitre les memes commandes que l'application, bascules de profil
        // comprises : un banc qui repondrait « je ne connais pas cette commande » a une phrase
        // qu'Optimus execute serait pire qu'inutile, il induirait en erreur.
        catalog = catalog with { Value = BindingProfileSet.Augment(catalog.Value) };
        LoadResult<Copilot> copilot = CopilotLoader.Load(
            Path.Combine(repoRoot, "data", "copilots", user.Value.PreferredCopilot));

        StarCitizenDetector detector = new();
        GameStatus game = detector.Detect();

        PrintHeader(catalog, profile, copilot, user, game, real);

        await using ITextToSpeechProvider speech = SpeechFactory.For(copilot.Value, silent);

        if (listVoices)
        {
            foreach (VoiceInfo voice in await speech.GetVoicesAsync().ConfigureAwait(false))
            {
                string marker = voice.DisplayName == copilot.Value.Voice.VoiceId ? "  <- copilote" : string.Empty;
                Console.WriteLine($"  {voice}{marker}");
            }

            return 0;
        }

        if (status)
        {
            PrintGameDetail(game);
            return 0;
        }

        // L'editeur de keybinds n'a besoin ni de voix ni de micro : il se traite avant que
        // quoi que ce soit de couteux ne demarre.
        if (showBindings || bindRequested || importRequested || exportLayout
            || unbindRequested || resetBindings)
        {
            IReadOnlyList<ActionSlot> slots = BindingEditor.Inventory(catalog.Value, profile.Value, overlay);

            // Le jeu range ses profils tres loin dans son arborescence. Quand il tourne, on sait
            // ou : autant s'en servir plutot que de demander au pilote un chemin qu'il devrait
            // aller chercher a la main.
            string? mappings = StarCitizenDetector.ResolveMappingsDirectory(game.ExecutablePath);

            if (resetBindings)
            {
                return BindingEditor.Reset(overlay, overlayPath);
            }

            if (unbindRequested)
            {
                return BindingEditor.Unassign(unbindTarget, slots, overlay, overlayPath);
            }

            if (importRequested)
            {
                return BindingEditor.ImportLayout(importLayout, slots, overlay, overlayPath, mappings);
            }

            if (bindRequested)
            {
                // « --bind » sans cible passe tout en revue : vingt et une actions a configurer
                // une par une, c'est vingt et une invocations que personne ne fera.
                return bindTarget is null
                    ? await BindingEditor.AssignAllAsync(slots, overlay, overlayPath).ConfigureAwait(false)
                    : await BindingEditor.AssignAsync(bindTarget, slots, overlay, overlayPath).ConfigureAwait(false);
            }

            if (exportLayout)
            {
                bool inGameFolder = exportPath is null && mappings is not null && Directory.Exists(mappings);

                string target = exportPath
                    ?? (inGameFolder
                        ? Path.Combine(mappings!, "optimus.xml")
                        : Path.Combine(Environment.CurrentDirectory, "optimus.xml"));

                return BindingEditor.ExportLayout(overlay, target, inGameFolder);
            }

            BindingEditor.PrintInventory(slots, overlay, mappings);
            return 0;
        }

        // Le moteur est prechauffe des le demarrage : la premiere synthese coute jusqu'a
        // 429 ms, et ce serait justement la premiere phrase entendue (D23).
        if (!silent)
        {
            await speech.WarmUpAsync(copilot.Value.Voice.VoiceId).ConfigureAwait(false);
        }

        ResponseComposer composer = new(copilot.Value.Personality, copilot.Value.Responses);

        if (real && !EnsureRealModeIsSafe(game))
        {
            return 2;
        }

        SimulatedInputEngine? simulation = real ? null : new SimulatedInputEngine();
        using IInputEngine engine = real ? new SendInputEngine() : simulation!;

        FastIntentMatcher matcher = new(catalog.Value);
        CommandExecutor executor = new(catalog.Value, profile.Value, engine, matcher);

        if (args.Contains("--listen", StringComparer.OrdinalIgnoreCase))
        {
            return await ListenAsync(
                executor, detector, simulation, composer, speech,
                copilot.Value, user.Value, catalog.Value, real).ConfigureAwait(false);
        }

        if (rest.Length > 0)
        {
            await RunAsync(executor, detector, simulation, composer, speech, copilot.Value,
                string.Join(' ', rest), real).ConfigureAwait(false);
            return 0;
        }

        Console.WriteLine("Tape une phrase, « ? » pour la liste des commandes, Entrée vide pour quitter.");
        Console.WriteLine();

        while (true)
        {
            Console.Write(real ? "réel > " : "> ");
            string? line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
            {
                return 0;
            }

            if (line.Trim() == "?")
            {
                PrintCommands(catalog.Value, profile.Value);
                continue;
            }

            await RunAsync(executor, detector, simulation, composer, speech, copilot.Value, line, real)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Écoute du micro jusqu'à interruption.
    ///
    /// C'est ici que les deux modes prennent corps : en écoute permanente, la grammaire n'accepte
    /// que les phrases commençant par le mot d'éveil ; en push-to-talk, elle accepte les deux
    /// formes mais reste désactivée hors appui.
    /// </summary>
    private static async Task<int> ListenAsync(
        CommandExecutor executor,
        StarCitizenDetector detector,
        SimulatedInputEngine? simulation,
        ResponseComposer composer,
        ITextToSpeechProvider speech,
        Copilot copilot,
        UserProfile user,
        CommandCatalog catalog,
        bool real)
    {
        VoiceInputSettings settings = user.VoiceInput;
        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, copilot.WakeWord, settings);

        await using WindowsGrammarListener listener = new(
            grammar, settings.ConfidenceThreshold, settings.NoiseFloor, copilot.Language);

        Console.WriteLine($"moteur        : {listener.RecognizerName}");
        Console.WriteLine($"grammaire     : {grammar.Count} alternatives" +
                          $"{(grammar.WakeWordRequired ? $", « {copilot.WakeWord} » obligatoire" : ", mot d'éveil facultatif")}");
        Console.WriteLine($"seuils        : bruit sous {settings.NoiseFloor:F2}" +
                          $" · exécution à partir de {settings.ConfidenceThreshold:F2}");
        Console.WriteLine();

        using PushToTalkWatcher? pushToTalk = settings.Mode == ListeningMode.PushToTalk
            ? new PushToTalkWatcher(settings.PushToTalkKey)
            : null;

        if (pushToTalk is not null)
        {
            // Hors appui, la grammaire est desactivee : le moteur n'a plus rien a reconnaitre.
            listener.SetActive(false);
            pushToTalk.StateChanged += (_, pressed) =>
            {
                listener.SetActive(pressed);
                Console.WriteLine(pressed ? "  [micro ouvert]" : "  [micro fermé]");
            };
            pushToTalk.Start();

            Console.WriteLine($"Maintiens {settings.PushToTalkKey} et parle. Ctrl+C pour quitter.");
        }
        else
        {
            Console.WriteLine($"Dis « {copilot.WakeWord}, ... ». Ctrl+C pour quitter.");
        }

        Console.WriteLine();

        using SemaphoreSlim processing = new(1, 1);

        // Commande proposée mais pas encore confirmée. Les confiances des vraies commandes et
        // des phrases hors catalogue se chevauchent — 0,55 pour une commande valide, 0,64 pour
        // une question inconnue — aucun seuil ne peut donc les séparer. Plutôt que de refuser,
        // Optimus propose et attend un « Optimus, confirme ».
        CommandDefinition? pending = null;
        string? pendingUtterance = null;
        CommandPolarity pendingPolarity = CommandPolarity.Neutral;
        DateTimeOffset pendingUntil = DateTimeOffset.MinValue;
        TimeSpan pendingLifetime = TimeSpan.FromSeconds(12);

        listener.Recognized += async (_, recognition) =>
        {
            // Une seule commande traitee a la fois : deux sequences d'entrees qui se
            // chevaucheraient enverraient des touches entremelees au jeu.
            if (!await processing.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                switch (recognition.Outcome)
                {
                    case RecognitionOutcome.Noise:
                        // Bruit ambiant ou parole hors grammaire : le cas de loin le plus
                        // frequent en ecoute permanente, et le seul ou se taire est la bonne
                        // reponse.
                        if (!string.IsNullOrWhiteSpace(recognition.Text))
                        {
                            Console.WriteLine($"  (ignoré) {recognition}");
                        }

                        return;

                    case RecognitionOutcome.Unclear:
                        Console.WriteLine();
                        Console.WriteLine($"  entendu     {recognition}");

                        // Une commande a bien ete reconnue, sans assez de certitude pour agir.
                        // On la propose : refuser une commande valide est aussi penible
                        // qu'executer celle qu'on n'a pas demandee.
                        if (recognition.CommandId is not null &&
                            catalog.TryGet(recognition.CommandId, out CommandDefinition? candidate) &&
                            candidate is not null)
                        {
                            pending = candidate;
                            pendingUtterance = recognition.Text;
                            pendingPolarity = recognition.Polarity;
                            pendingUntil = DateTimeOffset.UtcNow + pendingLifetime;

                            await SayAsync(composer, speech, copilot, ["system.propose"], ResponseEvent.Clarify,
                                new Dictionary<string, string> { ["command"] = candidate.Name })
                                .ConfigureAwait(false);
                            return;
                        }

                        await SayAsync(composer, speech, copilot,
                            ["system.unknown_command"], ResponseEvent.Unknown).ConfigureAwait(false);
                        return;

                    case RecognitionOutcome.Accepted:
                    default:
                        Console.WriteLine();
                        Console.WriteLine($"  entendu     {recognition}");

                        bool pendingAlive = pending is not null && DateTimeOffset.UtcNow <= pendingUntil;

                        if (recognition.CommandId == "system.confirm" && pendingAlive)
                        {
                            CommandDefinition confirmed = pending!;
                            string? confirmedUtterance = pendingUtterance;
                            CommandPolarity confirmedPolarity = pendingPolarity;
                            pending = null;
                            pendingUtterance = null;
                            pendingPolarity = CommandPolarity.Neutral;

                            Console.WriteLine($"  confirmé    {confirmed.Name}");
                            await RunCommandAsync(executor, detector, simulation, composer, speech, copilot,
                                confirmed, real, confirmedUtterance, confirmedPolarity).ConfigureAwait(false);
                            return;
                        }

                        if (recognition.CommandId == "system.deny" && pendingAlive)
                        {
                            pending = null;
                            await SayAsync(composer, speech, copilot, ["system.deny"], ResponseEvent.Any)
                                .ConfigureAwait(false);
                            return;
                        }

                        // Toute autre commande annule la proposition en attente : l'utilisateur
                        // est passe a autre chose.
                        pending = null;

                        await RunAsync(executor, detector, simulation, composer, speech, copilot,
                            recognition.Text, real).ConfigureAwait(false);
                        return;
                }
            }
            finally
            {
                processing.Release();
            }
        };

        await listener.StartAsync().ConfigureAwait(false);

        using CancellationTokenSource stop = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        try
        {
            await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Écoute interrompue.");
        }

        await listener.StopAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Chemin du profil utilisateur, fixe pour toute l'execution.
    ///
    /// Un champ statique plutot qu'un parametre de plus : « RunAsync » est appelee de trois
    /// endroits, et faire voyager un chemin constant a travers huit arguments pour l'usage
    /// d'une seule ligne aurait alourdi les trois pour n'en servir qu'une.
    /// </summary>
    private static string _userProfilePath = string.Empty;

    private static async Task RunAsync(
        CommandExecutor executor,
        StarCitizenDetector detector,
        SimulatedInputEngine? simulation,
        ResponseComposer composer,
        ITextToSpeechProvider speech,
        Copilot copilot,
        string utterance,
        bool real)
    {
        simulation?.Reset();

        // L'environnement est ré-observé à chaque énoncé : le jeu a pu perdre le focus entre
        // deux commandes, et c'est justement ce que le garde doit voir.
        GameStatus game = real ? detector.Detect() : GameStatus.NotRunning;
        ForgetBeliefIfGameRestarted(executor, game);

        ExecutionEnvironment environment = real
            ? new ExecutionEnvironment(
                SimulationMode: false,
                GameRunning: game.IsRunning,
                GameForeground: game.IsForeground,
                RequireGameForeground: true,
                CombatActive: State.CombatActive)
            : ExecutionEnvironment.Sandbox with { CombatActive = State.CombatActive };

        ExecutionResult result = await executor
            .ExecuteUtteranceAsync(
                utterance,
                environment,
                wakeWord: "Optimus",
                sequenceOptions: real ? new SequenceOptions(RealTime: true) : SequenceOptions.Instant)
            .ConfigureAwait(false);

        State.Record(result);

        // Bascule de profil de touches. Le banc l'applique reellement plutot que de se contenter
        // de reconnaitre la phrase : une trace qui affiche « Allowed » sans que rien ne change
        // serait plus trompeuse qu'un refus franc.
        if (BindingProfileSet.ProfileOf(result.Command) is string switched && result.Succeeded)
        {
            SettingsWriter.SaveActiveBindingProfile(
                _userProfilePath, switched);

            Console.WriteLine($"  profil      « {switched} » sera actif au prochain enonce");
        }

        // Bascule declarative du mode de combat : faute de telemetrie, Optimus se fie a ce que
        // le pilote lui annonce. Un IGameStateProvider prendra le relais le jour venu.
        if (result.Command?.Id == MasterMode.CommandId && result.Succeeded)
        {
            bool combat = State.ApplyMasterMode(result.Polarity, result.Intent?.NormalizedText);
            Console.WriteLine($"  contexte    mode {(combat ? "COMBAT" : "navigation")}");
        }

        Console.WriteLine();
        Console.WriteLine(result.Describe());

        if (simulation is not null && simulation.Events.Count > 0)
        {
            Console.WriteLine("  entrées simulées");
            foreach (string line in simulation.Transcript().Split(Environment.NewLine))
            {
                Console.WriteLine($"    {line}");
            }
        }

        if (simulation is not null && simulation.StillPressed.Count > 0)
        {
            Console.WriteLine($"  ANOMALIE : {simulation.StillPressed.Count} entrée(s) encore enfoncée(s)");
        }

        // La parole vient APRES l'action, jamais avant : une synthese lente doit degrader le
        // confort, jamais la reactivite du jeu (docs/09).
        ResponseRequest? request = ResponseRouter.Route(result, State.Snapshot());

        if (request is not null)
        {
            await SayAsync(composer, speech, copilot, request.Keys, request.Event, request.Variables)
                .ConfigureAwait(false);
        }

        Console.WriteLine();
    }

    /// <summary>Exécute une commande déjà identifiée, sans repasser par la résolution d'intention.</summary>
    private static async Task RunCommandAsync(
        CommandExecutor executor,
        StarCitizenDetector detector,
        SimulatedInputEngine? simulation,
        ResponseComposer composer,
        ITextToSpeechProvider speech,
        Copilot copilot,
        CommandDefinition command,
        bool real,
        string? utterance = null,
        CommandPolarity polarity = CommandPolarity.Neutral)
    {
        simulation?.Reset();

        GameStatus game = real ? detector.Detect() : GameStatus.NotRunning;
        ForgetBeliefIfGameRestarted(executor, game);

        ExecutionEnvironment environment = real
            ? new ExecutionEnvironment(
                SimulationMode: false,
                GameRunning: game.IsRunning,
                GameForeground: game.IsForeground,
                RequireGameForeground: true,
                CombatActive: State.CombatActive)
            : ExecutionEnvironment.Sandbox with { CombatActive = State.CombatActive };

        ExecutionResult result = await executor
            .ExecuteCommandAsync(
                command,
                environment,
                real ? new SequenceOptions(RealTime: true) : SequenceOptions.Instant,
                polarity: polarity)
            .ConfigureAwait(false);

        State.Record(result);

        // Meme bascule declarative que sur le chemin direct : une commande confirmee apres
        // proposition doit compter autant qu'une commande comprise du premier coup.
        if (command.Id == MasterMode.CommandId && result.Succeeded)
        {
            bool combat = State.ApplyMasterMode(polarity, utterance);
            Console.WriteLine($"  contexte    mode {(combat ? "COMBAT" : "navigation")}");
        }

        Console.WriteLine(result.Describe());

        ResponseRequest? request = ResponseRouter.Route(result, State.Snapshot());
        if (request is not null)
        {
            await SayAsync(composer, speech, copilot, request.Keys, request.Event, request.Variables)
                .ConfigureAwait(false);
        }

        Console.WriteLine();
    }

    /// <summary>État de session du copilote, alimenté par chaque exécution.</summary>
    private static readonly CopilotState State = new();

    /// <summary>Processus de jeu observe au dernier appel, pour reperer un redemarrage.</summary>
    private static int? LastGamePid;

    /// <summary>
    /// Compose une réplique et la prononce, après arbitrage des règles de comportement.
    ///
    /// C'est ici que le caractère du copilote rencontre la situation : les règles peuvent
    /// imposer la brièveté, interdire l'humour, ou substituer une réplique plus utile — par
    /// exemple cesser de constater un échec pour en donner la cause.
    /// </summary>
    private static async Task SayAsync(
        ResponseComposer composer,
        ITextToSpeechProvider speech,
        Copilot copilot,
        IReadOnlyList<string> keys,
        ResponseEvent responseEvent,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        CopilotContext context = State.Snapshot();
        EffectiveBehavior behavior = BehaviorEngine.Resolve(copilot.Personality.Rules, context, responseEvent);

        // Les cles suggerees par les regles passent devant : « troisieme echec » est plus utile
        // que « negatif » repete une fois de plus.
        IReadOnlyList<string> effectiveKeys = behavior.PreferredKeys.Count > 0
            ? behavior.PreferredKeys.Concat(keys).ToList()
            : keys;

        ComposedResponse? spoken = composer.ComposeFirst(
            effectiveKeys,
            responseEvent,
            variables,
            ResponseContext.From(behavior, context.CombatActive));

        if (spoken is null)
        {
            return;
        }

        string rules = behavior.AppliedRules.Count > 0
            ? $"   [{string.Join(", ", behavior.AppliedRules)}]"
            : string.Empty;

        Console.WriteLine($"  {copilot.Name,-11} « {spoken.Text} »   ({spoken.CandidateCount} variantes){rules}");

        await speech.SpeakAsync(
            new SpeechRequest(spoken.Text, copilot.Voice.VoiceId, copilot.EffectiveRate, copilot.Voice.Volume))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refuse le mode réel tant que les conditions ne sont pas réunies, et explique pourquoi.
    ///
    /// Le point le plus utile est la comparaison d'élévation : si le jeu est administrateur et
    /// pas Optimus, Windows filtre silencieusement les entrées. Sans ce message, l'utilisateur
    /// verrait des commandes « réussies » sans le moindre effet en jeu.
    /// </summary>
    private static bool EnsureRealModeIsSafe(GameStatus game)
    {
        if (!game.IsRunning)
        {
            Console.Error.WriteLine("Mode réel refusé : Star Citizen n'est pas lancé.");
            return false;
        }

        bool optimusElevated = StarCitizenDetector.IsCurrentProcessElevated();

        if (game.IsElevated is true && !optimusElevated)
        {
            Console.Error.WriteLine(
                "Mode réel refusé : Star Citizen tourne en administrateur et pas Optimus. " +
                "Windows bloquerait les entrées. Relance ce programme en administrateur.");
            return false;
        }

        Console.WriteLine("MODE RÉEL : les touches seront réellement envoyées.");
        Console.WriteLine("Place-toi dans le jeu, vaisseau posé, avant de lancer une commande.");
        Console.WriteLine();

        return true;
    }

    private static void PrintHeader(
        LoadResult<CommandCatalog> catalog,
        LoadResult<BindingProfile> profile,
        LoadResult<Copilot> copilot,
        LoadResult<UserProfile> user,
        GameStatus game,
        bool real)
    {
        VoiceInputSettings listening = user.Value.VoiceInput;

        string mode = listening.Mode == ListeningMode.AlwaysOn
            ? $"écoute permanente, déclenchée par « {copilot.Value.WakeWord} »"
            : $"push-to-talk sur {listening.PushToTalkKey}" +
              (listening.RequireWakeWordInPushToTalk ? " + mot d'éveil" : string.Empty);

        Console.WriteLine("+--------------------------------------------------------------+");
        Console.WriteLine($"|  OPTIMUS - banc d'essai du moteur  [{(real ? "MODE RÉEL " : "simulation")}]              |");
        Console.WriteLine("+--------------------------------------------------------------+");
        Console.WriteLine($"binaire       : {BuildStamp()}");
        Console.WriteLine($"copilote      : {copilot.Value.Name} · {copilot.Value.Voice.VoiceId ?? "voix par défaut"}" +
                          $" · débit {copilot.Value.EffectiveRate:F2}");
        Console.WriteLine($"synthèse      : {SpeechFactory.Describe(copilot.Value)}");
        Console.WriteLine($"personnalité  : {copilot.Value.Responses.EntryCount} entrées, " +
                          $"{copilot.Value.Responses.VariantCount} variantes · " +
                          $"{copilot.Value.Personality.Traits.MaxWords} mots max");
        Console.WriteLine($"écoute        : {mode}");
        Console.WriteLine($"catalogue     : {catalog.Value.Count} commandes");
        Console.WriteLine($"bindings      : {profile.Value.BoundCount} actions liées, {profile.Value.UnboundCount} sans touche" +
                          $"  (jeu {profile.Value.GameVersion}, build {profile.Value.GameBuild})");
        string bindings = BindingProfileSet.Resolve(user.Value.ActiveBindingProfile);

        Console.WriteLine(
            $"profil touches: « {bindings} » · "
            + $"{BindingOverlay.Load(BindingProfileSet.PathOf(bindings)).Count} assignations"
            + $"  ({BindingProfileSet.List().Count} profils installés)");
        Console.WriteLine($"scancodes     : {ScanCodeMap.Count} touches connues");

        // Ce banc monte son propre executeur, pas `OptimusRuntime` : l'escalade vers le modele
        // n'existe donc pas ici, meme quand l'etage est configure. Le dire, plutot que d'afficher
        // un reglage qui laisserait croire qu'un enonce inconnu part au modele depuis le banc.
        Optimus.Core.Ai.AiSettings ai = user.Value.Ai ?? Optimus.Core.Ai.AiSettings.Disabled;

        Console.WriteLine($"étage LLM     : {(ai.Enabled
            ? $"{ai.Provider}:{ai.Model} sur {ai.Endpoint} — configuré, mais non sollicité par ce banc"
            : "désactivé — tout reste hors ligne")}");
        Console.WriteLine($"Star Citizen  : {game}");

        foreach (LoadIssue issue in catalog.Issues.Concat(profile.Issues).Concat(copilot.Issues).Concat(user.Issues))
        {
            Console.WriteLine($"  anomalie de chargement : {issue}");
        }

        Console.WriteLine();
    }

    private static void PrintGameDetail(GameStatus game)
    {
        Console.WriteLine($"  lancé          : {(game.IsRunning ? "oui" : "non")}");
        Console.WriteLine($"  premier plan   : {(game.IsForeground ? "oui" : "non")}");
        Console.WriteLine($"  pid            : {game.ProcessId?.ToString() ?? "-"}");
        Console.WriteLine($"  exécutable     : {game.ExecutablePath ?? "-"}");
        Console.WriteLine($"  canal          : {game.Channel ?? "-"}");
        Console.WriteLine($"  élévation jeu  : {(game.IsElevated is null ? "inconnue" : game.IsElevated.Value ? "oui" : "non")}");
        Console.WriteLine($"  élévation ici  : {(StarCitizenDetector.IsCurrentProcessElevated() ? "oui" : "non")}");

        string? channelDirectory = StarCitizenDetector.ResolveChannelDirectory(game.ExecutablePath);
        Console.WriteLine($"  dossier canal  : {channelDirectory ?? "-"}");
        Console.WriteLine();
    }

    private static void PrintCommands(CommandCatalog catalog, BindingProfile bindings)
    {
        foreach (IGrouping<string, CommandDefinition> group in catalog.Commands
                     .GroupBy(c => c.Category)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  [{group.Key}]");

            foreach (CommandDefinition command in group.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                Console.WriteLine($"    {command.Id,-32} {DescribeBinding(command, bindings),-24} « {command.VoicePhrases[0]} »");
            }
        }

        Console.WriteLine();
    }

    private static string DescribeBinding(CommandDefinition command, BindingProfile bindings)
    {
        if (command.IsPassive)
        {
            return "(aucune touche)";
        }

        List<string> parts = new();

        foreach (string actionId in command.ReferencedActionIds)
        {
            BindingLookup lookup = bindings.Resolve(actionId, out Binding? binding);
            parts.Add(lookup switch
            {
                BindingLookup.Bound when binding is not null => binding.Input.ToString(),
                BindingLookup.NotBound => "A CONFIGURER",
                BindingLookup.UnknownAction => "action inconnue",
                BindingLookup.Unsupported => "non injectable",
                _ => "?",
            });
        }

        return string.Join(" + ", parts);
    }

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);

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

    /// <summary>
    /// Un nouveau processus de jeu, c'est un vaisseau reparti d'un état neuf : ce qu'Optimus
    /// croyait savoir des bascules ne vaut plus rien, et serait faux plus souvent que juste.
    /// </summary>
    private static void ForgetBeliefIfGameRestarted(CommandExecutor executor, GameStatus game)
    {
        if (!game.IsRunning)
        {
            LastGamePid = null;
            return;
        }

        if (game.ProcessId != LastGamePid)
        {
            executor.Belief.Forget();
            LastGamePid = game.ProcessId;
        }
    }

    /// <summary>
    /// Version et date de compilation du binaire.
    ///
    /// Affiche en tete parce que le paquet se recopie a la main d'une machine a l'autre : sans
    /// ce reperage, rien ne distingue a l'oeil une version de la precedente, et l'on cherche
    /// pendant dix minutes pourquoi une option « ajoutee » reste introuvable.
    /// </summary>
    private static string BuildStamp()
    {
        System.Reflection.Assembly assembly = typeof(Program).Assembly;

        string version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string built = "date inconnue";

        try
        {
            // Le chemin du processus, et non celui de l'assembly : publie en fichier unique,
            // `Assembly.Location` rend une chaine vide (IL3000), et le repere de date
            // disparaitrait justement dans la version qu'on distribue.
            string? executable = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(executable) && File.Exists(executable))
            {
                built = File.GetLastWriteTime(executable).ToString("yyyy-MM-dd HH:mm");
            }
        }
        catch (IOException)
        {
            // Le repere est un confort : son absence ne doit jamais empecher le demarrage.
        }

        return $"{version} · compilé le {built}";
    }

    /// <summary>
    /// Valeur d'une option, ecrite « --option valeur » ou « --option=valeur ».
    /// Retourne une chaine vide quand l'option est presente sans valeur, et <c>null</c> quand
    /// elle est absente : les deux cas ne veulent pas dire la meme chose.
    /// </summary>
    private static string? OptionValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];

            if (argument.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(name.Length + 1)..];
            }

            if (!string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            return hasValue ? args[i + 1] : null;
        }

        return null;
    }
}
