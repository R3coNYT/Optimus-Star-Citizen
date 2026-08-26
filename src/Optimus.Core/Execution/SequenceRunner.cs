using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>Étape de séquence telle qu'exécutée, pour la trace et le mode debug.</summary>
/// <param name="Index">Rang de l'étape.</param>
/// <param name="Description">Ce qui a été fait, en clair.</param>
/// <param name="DurationMs">Temps passé sur l'étape.</param>
public sealed record SequenceStepTrace(int Index, string Description, double DurationMs);

/// <summary>Options d'exécution d'une séquence.</summary>
/// <param name="RealTime">
/// Si faux, les attentes sont consignées mais pas subies. Une séquence de deux secondes
/// s'exécute alors instantanément : c'est ce qui rend les tests rapides et le mode simulation
/// agréable, sans changer une ligne du reste du moteur.
/// </param>
/// <param name="PreciseTiming">
/// Termine les attentes en attente active plutôt qu'en <c>Task.Delay</c> seul.
///
/// Mesuré en jeu le 2026-08-25 : un maintien demandé à 45 ms en durait 96, la granularité du
/// minuteur système étant d'environ 15 ms. Sans conséquence sur un <c>tap</c>, que Star Citizen
/// tolère jusqu'à 250 ms — mais inacceptable pour les séquences dont le rythme compte, et
/// contraire à la décision D20. Le spike S0-1 avait obtenu ±0,4 ms par cette méthode.
/// </param>
public sealed record SequenceOptions(bool RealTime = true, bool PreciseTiming = true)
{
    public static SequenceOptions Instant { get; } = new(RealTime: false);
}

/// <summary>
/// Exécute une suite d'étapes sur un <see cref="IInputEngine"/>.
///
/// Sa responsabilité la plus importante n'est pas d'appuyer sur les touches, mais de garantir
/// qu'aucune ne reste enfoncée — sur exception, sur annulation, sur arrêt d'urgence. D'où le
/// <c>try/finally</c> qui enveloppe la totalité du parcours.
/// </summary>
public sealed class SequenceRunner
{
    private readonly IInputEngine _engine;
    private readonly BindingProfile _bindings;

    private readonly Func<string, CancellationToken, Task>? _narrate;

    /// <param name="narrate">
    /// Prononce une réplique au milieu d'une séquence, désignée par sa clé. Optionnel : le
    /// moteur reste utilisable sans voix, ce dont les tests profitent. En simulation, la
    /// narration reste active — ne pas envoyer de touches n'est pas une raison de se taire,
    /// et c'est même ainsi qu'on vérifie une macro avant de la lancer pour de vrai.
    /// </param>
    public SequenceRunner(
        IInputEngine engine,
        BindingProfile bindings,
        Func<string, CancellationToken, Task>? narrate = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _narrate = narrate;
    }

    /// <summary>
    /// Exécute les étapes. Retourne la trace de ce qui a été fait, ou lève si une étape
    /// référence une action non résolvable — cas que l'appelant doit avoir écarté en amont.
    /// </summary>
    public async Task<IReadOnlyList<SequenceStepTrace>> RunAsync(
        IReadOnlyList<ActionStep> steps,
        SequenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        options ??= new SequenceOptions();

        List<SequenceStepTrace> trace = new(steps.Count);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            for (int index = 0; index < steps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                string description = await ExecuteStepAsync(steps[index], options, cancellationToken)
                    .ConfigureAwait(false);

                trace.Add(new SequenceStepTrace(
                    index,
                    description,
                    Elapsed(stepStart)));
            }
        }
        finally
        {
            // Inconditionnel : c'est la seule garantie qui vaille dans un vaisseau en vol.
            await _engine.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _ = Elapsed(start);
        return trace;
    }

    private async Task<string> ExecuteStepAsync(
        ActionStep step, SequenceOptions options, CancellationToken cancellationToken)
    {
        switch (step.Type)
        {
            case ActionStepType.Wait:
                await DelayAsync(step.WaitMs, options, cancellationToken).ConfigureAwait(false);
                return $"attendre {step.WaitMs} ms";

            case ActionStepType.Say:
                // Une macro de dix secondes qui se deroulerait en silence est inquietante :
                // c'est precisement pendant une sequence longue qu'on veut etre accompagne.
                // Faute de narrateur, on trace l'intention plutot que de la perdre.
                if (_narrate is not null && step.ResponseKey is not null)
                {
                    await _narrate(step.ResponseKey, cancellationToken).ConfigureAwait(false);
                }

                return $"dire « {step.ResponseKey} »";

            case ActionStepType.Key:
                if (step.RawInput is null)
                {
                    throw new InvalidOperationException("Étape de type Key sans entrée.");
                }

                await SendAsync(step.RawInput, options, cancellationToken).ConfigureAwait(false);
                return step.RawInput.ToString();

            case ActionStepType.GameAction:
                {
                    if (step.ActionId is null)
                    {
                        throw new InvalidOperationException("Étape de type GameAction sans identifiant d'action.");
                    }

                    BindingLookup lookup = _bindings.Resolve(step.ActionId, out Binding? binding);
                    if (lookup != BindingLookup.Bound || binding is null)
                    {
                        throw new InvalidOperationException(
                            $"L'action « {step.ActionId} » n'est pas exécutable ({lookup}).");
                    }

                    InputSpec input = ApplyOverrides(binding.Input, step);
                    await SendAsync(input, options, cancellationToken).ConfigureAwait(false);
                    return $"{step.ActionId} → {input}";
                }

            default:
                throw new NotSupportedException($"Type d'étape non pris en charge : {step.Type}");
        }
    }

    /// <summary>
    /// Une commande peut imposer un mode ou une durée différents de ceux du binding : par
    /// exemple maintenir le ping radar plus longtemps que le minimum exigé par le jeu.
    /// </summary>
    private static InputSpec ApplyOverrides(InputSpec input, ActionStep step) =>
        input with
        {
            Mode = step.Mode ?? input.Mode,
            HoldMs = step.HoldMs ?? input.HoldMs,
            Repeat = step.Repeat > 1 ? step.Repeat : input.Repeat,
            IntervalMs = step.IntervalMs,
        };

    private async Task SendAsync(InputSpec input, SequenceOptions options, CancellationToken cancellationToken)
    {
        int repeats = Math.Max(1, input.Repeat);

        for (int iteration = 0; iteration < repeats; iteration++)
        {
            if (iteration > 0)
            {
                await DelayAsync(input.IntervalMs, options, cancellationToken).ConfigureAwait(false);
            }

            switch (input.Mode)
            {
                case InputMode.Press:
                    await _engine.PressAsync(input, cancellationToken).ConfigureAwait(false);
                    break;

                case InputMode.Release:
                    await _engine.ReleaseAsync(input, cancellationToken).ConfigureAwait(false);
                    break;

                case InputMode.DoubleTap:
                    await TapAsync(input, InputDefaults.HoldMs, options, cancellationToken).ConfigureAwait(false);
                    await DelayAsync(InputDefaults.DoubleTapGapMs, options, cancellationToken).ConfigureAwait(false);
                    await TapAsync(input, InputDefaults.HoldMs, options, cancellationToken).ConfigureAwait(false);
                    break;

                case InputMode.Hold:
                case InputMode.Tap:
                default:
                    await TapAsync(input, input.HoldMs, options, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task TapAsync(InputSpec input, int holdMs, SequenceOptions options, CancellationToken cancellationToken)
    {
        await _engine.PressAsync(input, cancellationToken).ConfigureAwait(false);
        try
        {
            await DelayAsync(holdMs, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _engine.ReleaseAsync(input, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attente précise : sommeil grossier pour l'essentiel, attente active pour la fin.
    ///
    /// Le sommeil seul dépasse systématiquement la cible d'une dizaine de millisecondes ;
    /// l'attente active seule brûlerait un cœur. La marge de 12 ms couvre la granularité du
    /// minuteur sans monopoliser le processeur — ressource que le jeu se dispute déjà.
    /// </summary>
    private static async Task DelayAsync(int milliseconds, SequenceOptions options, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0 || !options.RealTime)
        {
            return;
        }

        if (!options.PreciseTiming)
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
            return;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int coarse = milliseconds - TimerGranularityMarginMs;

        if (coarse > 0)
        {
            await Task.Delay(coarse, cancellationToken).ConfigureAwait(false);
        }

        while (Elapsed(start) < milliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.SpinWait(50);
        }
    }

    /// <summary>Marge réservée à l'attente active, couvrant la granularité du minuteur système.</summary>
    private const int TimerGranularityMarginMs = 12;

    private static double Elapsed(long since) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}
