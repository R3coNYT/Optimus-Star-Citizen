using System.Diagnostics;
using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Execution;

/// <summary>Issue d'une demande d'exécution.</summary>
public enum ExecutionStatus
{
    /// <summary>Entrées réellement envoyées au jeu.</summary>
    Executed,

    /// <summary>Séquence déroulée en simulation : rien n'a été envoyé.</summary>
    Simulated,

    /// <summary>Commande passive : une réponse, aucune entrée.</summary>
    Answered,

    /// <summary>Refusée par le garde.</summary>
    Rejected,

    /// <summary>Énoncé non compris.</summary>
    Unknown,

    /// <summary>Compris mais ambigu : il faut demander.</summary>
    NeedsClarification,

    /// <summary>Échec en cours de séquence.</summary>
    Failed,

    /// <summary>
    /// Rien à faire : la commande est déjà dans l'état demandé, au su d'Optimus. Ce n'est pas un
    /// échec — aucune touche n'a été envoyée parce qu'aucune n'était utile.
    /// </summary>
    NoChangeNeeded,
}

/// <summary>
/// Compte rendu complet d'un énoncé, de la transcription à l'appui de touche.
///
/// C'est cet objet que le mode debug affiche et que l'historique persiste : une commande qui
/// échoue doit toujours pouvoir expliquer où et pourquoi.
/// </summary>
public sealed record ExecutionResult(
    string TraceId,
    ExecutionStatus Status,
    IntentResolution? Intent,
    CommandDefinition? Command,
    GuardDecision? Guard,
    IReadOnlyList<SequenceStepTrace> Steps,
    double TotalMs,
    string? Message = null,
    CommandPolarity Polarity = CommandPolarity.Neutral,
    bool Narrated = false)
{
    public bool Succeeded => Status is ExecutionStatus.Executed or ExecutionStatus.Simulated
        or ExecutionStatus.Answered or ExecutionStatus.NoChangeNeeded;

    /// <summary>Rendu façon « PIPELINE TRACE » de docs/09.</summary>
    public string Describe()
    {
        System.Text.StringBuilder builder = new();
        builder.AppendLine($"trace {TraceId} · {Status} · {TotalMs:F1} ms");

        if (Intent is not null)
        {
            builder.AppendLine($"  énoncé      « {Intent.RawText} »");
            builder.AppendLine($"  normalisé   « {Intent.NormalizedText} »");

            if (Intent.Best is not null)
            {
                string sense = Polarity switch
                {
                    CommandPolarity.On => "  → activation",
                    CommandPolarity.Off => "  → extinction",
                    _ => string.Empty,
                };

                builder.AppendLine(
                    $"  intent      {Intent.Best.Command.Id}  score {Intent.Best.Score:F2}  ({Intent.Best.Kind}){sense}");
            }

            if (Intent.Candidates.Count > 1)
            {
                IntentCandidate second = Intent.Candidates[1];
                builder.AppendLine($"  2e          {second.Command.Id}  score {second.Score:F2}");
            }
        }

        if (Guard is not null)
        {
            builder.AppendLine($"  garde       {Guard.Verdict}{(Guard.Detail is null ? string.Empty : $" — {Guard.Detail}")}");
        }

        foreach (SequenceStepTrace step in Steps)
        {
            builder.AppendLine($"  étape {step.Index,-3} {step.Description}  ({step.DurationMs:F1} ms)");
        }

        if (Message is not null)
        {
            builder.AppendLine($"  message     {Message}");
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Orchestre le chemin complet : énoncé → intent → garde → binding → entrées.
///
/// C'est le seul composant autorisé à déclencher une exécution. Tout ce qui veut faire agir
/// Optimus — la voix, l'interface, l'API locale, Discord, un plugin — passe par ici et se
/// soumet donc au même <see cref="ExecutionGuard"/>.
/// </summary>
public sealed class CommandExecutor
{
    private readonly CommandCatalog _catalog;
    private readonly BindingProfile _bindings;
    private readonly FastIntentMatcher _matcher;
    private readonly ExecutionGuard _guard;
    private readonly IInputEngine _engine;
    private readonly ToggleBelief _belief;
    private readonly Func<string, CancellationToken, Task>? _narrate;

    public CommandExecutor(
        CommandCatalog catalog,
        BindingProfile bindings,
        IInputEngine engine,
        FastIntentMatcher? matcher = null,
        ExecutionGuard? guard = null,
        ToggleBelief? belief = null,
        Func<string, CancellationToken, Task>? narrate = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _matcher = matcher ?? new FastIntentMatcher(catalog);
        _guard = guard ?? new ExecutionGuard();
        _belief = belief ?? new ToggleBelief();
        _narrate = narrate;
    }

    /// <summary>État supposé des bascules. À oublier quand le jeu redémarre.</summary>
    public ToggleBelief Belief => _belief;

    /// <summary>Résout un énoncé puis l'exécute s'il est suffisamment clair.</summary>
    public async Task<ExecutionResult> ExecuteUtteranceAsync(
        string utterance,
        ExecutionEnvironment environment,
        string? wakeWord = "Optimus",
        SequenceOptions? sequenceOptions = null,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        string traceId = NewTraceId();
        long start = Stopwatch.GetTimestamp();

        IntentResolution intent = _matcher.Resolve(utterance, wakeWord);

        if (intent.Decision == IntentDecision.Unknown || intent.Best is null)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Unknown, intent, null, null,
                Array.Empty<SequenceStepTrace>(), Elapsed(start),
                "Je ne connais pas cette commande.");
        }

        if (intent.Decision is IntentDecision.Disambiguate or IntentDecision.Confirm)
        {
            string message = intent.Decision == IntentDecision.Disambiguate
                ? "Plusieurs commandes correspondent : laquelle ?"
                : $"Vous voulez dire « {intent.Best.Command.Name} » ?";

            return new ExecutionResult(
                traceId, ExecutionStatus.NeedsClarification, intent, intent.Best.Command, null,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), message);
        }

        ExecutionResult result = await ExecuteCommandAsync(
            intent.Best.Command, environment, sequenceOptions, confirmed, traceId, start,
            intent.Best.Polarity, cancellationToken)
            .ConfigureAwait(false);

        return result with { Intent = intent };
    }

    /// <summary>Exécute une commande déjà identifiée : interface, API, test, bouton « tester ».</summary>
    public async Task<ExecutionResult> ExecuteCommandAsync(
        CommandDefinition command,
        ExecutionEnvironment environment,
        SequenceOptions? sequenceOptions = null,
        bool confirmed = false,
        string? traceId = null,
        long? startTimestamp = null,
        CommandPolarity polarity = CommandPolarity.Neutral,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        traceId ??= NewTraceId();
        long start = startTimestamp ?? Stopwatch.GetTimestamp();

        // Sequence dirigee si le jeu en declare une ET qu'elle a une touche ; la bascule sinon.
        // Les renvois d'une macro sont deplies ICI, avant la garde : elle doit pouvoir verifier
        // la sequence complete avant qu'une seule touche ne parte, sans quoi une macro dont le
        // quatrieme pas manque de raccourci jouerait les trois premiers et laisserait le
        // vaisseau dans un etat que personne n'a demande.
        IReadOnlyList<ActionStep> steps;

        try
        {
            steps = MacroExpander.Expand(command, _catalog, _bindings, polarity);
        }
        catch (InvalidOperationException exception)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Failed, null, command, null,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), exception.Message, polarity);
        }

        // Se demande a la commande, pas a la liste depliee : le depliage cree une nouvelle
        // liste, et comparer les references ici aurait silencieusement toujours repondu « non ».
        bool directed = polarity != CommandPolarity.Neutral
            && command.UsesDirectedActions(polarity, _bindings);

        GuardDecision guard = _guard.Evaluate(command, _bindings, environment, confirmed, steps);
        if (!guard.IsAllowed)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Rejected, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), guard.Detail, polarity);
        }

        // Une action dirigee est idempotente : la reenvoyer ne peut pas nuire, donc rien a
        // supposer. C'est seulement sur une bascule qu'un appui de trop fait l'inverse.
        if (!directed && !command.IsPassive && _belief.IsRedundant(command.Id, polarity))
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.NoChangeNeeded, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start),
                $"« {command.Name} » : rien à changer.", polarity);
        }

        if (command.IsPassive)
        {
            _guard.MarkExecuted(command);
            return new ExecutionResult(
                traceId, ExecutionStatus.Answered, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), null, polarity);
        }

        SequenceRunner runner = new(_engine, _bindings, _narrate);

        try
        {
            IReadOnlyList<SequenceStepTrace> traces = await runner
                .RunAsync(steps, sequenceOptions, cancellationToken)
                .ConfigureAwait(false);

            _guard.MarkExecuted(command);
            _belief.RecordApplied(command.Id, polarity);

            ExecutionStatus status = _engine.IsReal && !environment.SimulationMode
                ? ExecutionStatus.Executed
                : ExecutionStatus.Simulated;

            // Une macro qui s'annonce elle-meme n'a pas besoin qu'on la felicite ensuite :
            // entendre « Vaisseau pare » puis « Conforme » sonne comme deux copilotes.
            bool narrated = steps.Any(step => step.Type == ActionStepType.Say);

            return new ExecutionResult(
                traceId, status, null, command, guard, traces, Elapsed(start), null, polarity, narrated);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Failed, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start),
                "Séquence interrompue ; toutes les touches ont été relâchées.", polarity);
        }
        catch (InvalidOperationException exception)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Failed, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), exception.Message, polarity);
        }
    }

    private static string NewTraceId() => Guid.NewGuid().ToString("N")[..8];

    private static double Elapsed(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
}
