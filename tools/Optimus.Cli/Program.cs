using Optimus.Core.Abstractions;
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

        bool real = args.Contains("--real", StringComparer.OrdinalIgnoreCase);
        bool status = args.Contains("--status", StringComparer.OrdinalIgnoreCase);
        bool silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        bool listVoices = args.Contains("--voices", StringComparer.OrdinalIgnoreCase);
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
        LoadResult<UserProfile> user = ProfileLoader.Load(Path.Combine(repoRoot, "data", "profiles", "default.json"));
        LoadResult<Copilot> copilot = CopilotLoader.Load(
            Path.Combine(repoRoot, "data", "copilots", user.Value.PreferredCopilot));

        StarCitizenDetector detector = new();
        GameStatus game = detector.Detect();

        PrintHeader(catalog, profile, copilot, user, game, real);

        await using ITextToSpeechProvider speech = silent
            ? new NullTextToSpeechProvider()
            : new WindowsTtsProvider();

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
                            pending = null;

                            Console.WriteLine($"  confirmé    {confirmed.Name}");
                            await RunCommandAsync(executor, detector, simulation, composer, speech, copilot,
                                confirmed, real).ConfigureAwait(false);
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

        ExecutionEnvironment environment = real
            ? new ExecutionEnvironment(
                SimulationMode: false,
                GameRunning: game.IsRunning,
                GameForeground: game.IsForeground,
                RequireGameForeground: true)
            : ExecutionEnvironment.Sandbox;

        ExecutionResult result = await executor
            .ExecuteUtteranceAsync(
                utterance,
                environment,
                wakeWord: "Optimus",
                sequenceOptions: real ? new SequenceOptions(RealTime: true) : SequenceOptions.Instant)
            .ConfigureAwait(false);

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
        ResponseRequest? request = ResponseRouter.Route(result);

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
        bool real)
    {
        simulation?.Reset();

        GameStatus game = real ? detector.Detect() : GameStatus.NotRunning;

        ExecutionEnvironment environment = real
            ? new ExecutionEnvironment(
                SimulationMode: false,
                GameRunning: game.IsRunning,
                GameForeground: game.IsForeground,
                RequireGameForeground: true)
            : ExecutionEnvironment.Sandbox;

        ExecutionResult result = await executor
            .ExecuteCommandAsync(
                command,
                environment,
                real ? new SequenceOptions(RealTime: true) : SequenceOptions.Instant)
            .ConfigureAwait(false);

        Console.WriteLine(result.Describe());

        ResponseRequest? request = ResponseRouter.Route(result);
        if (request is not null)
        {
            await SayAsync(composer, speech, copilot, request.Keys, request.Event, request.Variables)
                .ConfigureAwait(false);
        }

        Console.WriteLine();
    }

    /// <summary>Compose une réplique et la prononce.</summary>
    private static async Task SayAsync(
        ResponseComposer composer,
        ITextToSpeechProvider speech,
        Copilot copilot,
        IReadOnlyList<string> keys,
        ResponseEvent responseEvent,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        ComposedResponse? spoken = composer.ComposeFirst(keys, responseEvent, variables);

        if (spoken is null)
        {
            return;
        }

        Console.WriteLine($"  {copilot.Name,-11} « {spoken.Text} »   ({spoken.CandidateCount} variantes possibles)");

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
        Console.WriteLine($"copilote      : {copilot.Value.Name} · {copilot.Value.Voice.VoiceId ?? "voix par défaut"}" +
                          $" · débit {copilot.Value.EffectiveRate:F2}");
        Console.WriteLine($"personnalité  : {copilot.Value.Responses.EntryCount} entrées, " +
                          $"{copilot.Value.Responses.VariantCount} variantes · " +
                          $"{copilot.Value.Personality.Traits.MaxWords} mots max");
        Console.WriteLine($"écoute        : {mode}");
        Console.WriteLine($"catalogue     : {catalog.Value.Count} commandes");
        Console.WriteLine($"bindings      : {profile.Value.BoundCount} actions liées, {profile.Value.UnboundCount} sans touche" +
                          $"  (jeu {profile.Value.GameVersion}, build {profile.Value.GameBuild})");
        Console.WriteLine($"scancodes     : {ScanCodeMap.Count} touches connues");
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

}
