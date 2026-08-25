using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Core.Loading;
using Optimus.Infrastructure.Game;
using Optimus.Infrastructure.Input;

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

        StarCitizenDetector detector = new();
        GameStatus game = detector.Detect();

        PrintHeader(catalog, profile, game, real);

        if (status)
        {
            PrintGameDetail(game);
            return 0;
        }

        if (real && !EnsureRealModeIsSafe(game))
        {
            return 2;
        }

        SimulatedInputEngine? simulation = real ? null : new SimulatedInputEngine();
        using IInputEngine engine = real ? new SendInputEngine() : simulation!;

        FastIntentMatcher matcher = new(catalog.Value);
        CommandExecutor executor = new(catalog.Value, profile.Value, engine, matcher);

        if (rest.Length > 0)
        {
            await RunAsync(executor, detector, simulation, string.Join(' ', rest), real).ConfigureAwait(false);
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

            await RunAsync(executor, detector, simulation, line, real).ConfigureAwait(false);
        }
    }

    private static async Task RunAsync(
        CommandExecutor executor,
        StarCitizenDetector detector,
        SimulatedInputEngine? simulation,
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

        Console.WriteLine();
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
        LoadResult<CommandCatalog> catalog, LoadResult<BindingProfile> profile, GameStatus game, bool real)
    {
        Console.WriteLine("+--------------------------------------------------------------+");
        Console.WriteLine($"|  OPTIMUS - banc d'essai du moteur  [{(real ? "MODE RÉEL " : "simulation")}]              |");
        Console.WriteLine("+--------------------------------------------------------------+");
        Console.WriteLine($"catalogue     : {catalog.Value.Count} commandes");
        Console.WriteLine($"bindings      : {profile.Value.BoundCount} actions liées, {profile.Value.UnboundCount} sans touche" +
                          $"  (jeu {profile.Value.GameVersion}, build {profile.Value.GameBuild})");
        Console.WriteLine($"scancodes     : {ScanCodeMap.Count} touches connues");
        Console.WriteLine($"Star Citizen  : {game}");

        foreach (LoadIssue issue in catalog.Issues.Concat(profile.Issues))
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
