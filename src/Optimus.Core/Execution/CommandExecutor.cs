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
    string? Message = null)
{
    public bool Succeeded => Status is ExecutionStatus.Executed or ExecutionStatus.Simulated or ExecutionStatus.Answered;

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
                builder.AppendLine($"  intent      {Intent.Best.Command.Id}  score {Intent.Best.Score:F2}  ({Intent.Best.Kind})");
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
            builder.AppendLine($"  étape {step.Index}     {step.Description}  ({step.DurationMs:F1} ms)");
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

    public CommandExecutor(
        CommandCatalog catalog,
        BindingProfile bindings,
        IInputEngine engine,
        FastIntentMatcher? matcher = null,
        ExecutionGuard? guard = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _matcher = matcher ?? new FastIntentMatcher(catalog);
        _guard = guard ?? new ExecutionGuard();
    }

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
            intent.Best.Command, environment, sequenceOptions, confirmed, traceId, start, cancellationToken)
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        traceId ??= NewTraceId();
        long start = startTimestamp ?? Stopwatch.GetTimestamp();

        GuardDecision guard = _guard.Evaluate(command, _bindings, environment, confirmed);
        if (!guard.IsAllowed)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Rejected, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), guard.Detail);
        }

        if (command.IsPassive)
        {
            _guard.MarkExecuted(command);
            return new ExecutionResult(
                traceId, ExecutionStatus.Answered, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start));
        }

        SequenceRunner runner = new(_engine, _bindings);

        try
        {
            IReadOnlyList<SequenceStepTrace> steps = await runner
                .RunAsync(command.Actions, sequenceOptions, cancellationToken)
                .ConfigureAwait(false);

            _guard.MarkExecuted(command);

            ExecutionStatus status = _engine.IsReal && !environment.SimulationMode
                ? ExecutionStatus.Executed
                : ExecutionStatus.Simulated;

            return new ExecutionResult(traceId, status, null, command, guard, steps, Elapsed(start));
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Failed, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start),
                "Séquence interrompue ; toutes les touches ont été relâchées.");
        }
        catch (InvalidOperationException exception)
        {
            return new ExecutionResult(
                traceId, ExecutionStatus.Failed, null, command, guard,
                Array.Empty<SequenceStepTrace>(), Elapsed(start), exception.Message);
        }
    }

    private static string NewTraceId() => Guid.NewGuid().ToString("N")[..8];

    private static double Elapsed(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
}
