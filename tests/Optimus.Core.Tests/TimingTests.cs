using System.Diagnostics;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;

namespace Optimus.Core.Tests;

/// <summary>
/// Précision des durées produites par le <see cref="SequenceRunner"/>.
///
/// Le premier essai en jeu a montré un maintien demandé à 45 ms qui en durait 96 : la
/// granularité du minuteur système. Ces tests figent la correction, pour qu'une refonte
/// future du runner ne la reperde pas en silence.
/// </summary>
public sealed class TimingTests
{
    private static readonly BindingProfile Profile = new(
        "test", "Profil de test", "4.9", null,
        [new Binding("test/press", InputSpec.Simple("L"))],
        []);

    [Fact]
    public async Task Un_maintien_est_tenu_avec_une_marge_raisonnable()
    {
        const int requested = 120;

        SimulatedInputEngine engine = new();
        SequenceRunner runner = new(engine, Profile);

        ActionStep[] steps =
        [
            new(ActionStepType.GameAction, "test/press", Mode: InputMode.Hold, HoldMs: requested),
        ];

        Stopwatch clock = Stopwatch.StartNew();
        await runner.RunAsync(steps, new SequenceOptions(RealTime: true));
        clock.Stop();

        double actual = clock.Elapsed.TotalMilliseconds;

        // On vérifie l'ordre de grandeur, pas la milliseconde : une machine d'intégration
        // continue chargée n'est pas un banc de mesure. L'ancien comportement dépassait de
        // plus du double ; la borne haute le rattraperait.
        Assert.InRange(actual, requested - 5, requested + 40);
    }

    [Fact]
    public async Task Le_mode_instantane_ne_subit_aucune_attente()
    {
        // Le mode simulation doit rester immédiat : une séquence de plusieurs secondes ne doit
        // pas ralentir les tests ni l'aperçu proposé à l'utilisateur.
        SimulatedInputEngine engine = new();
        SequenceRunner runner = new(engine, Profile);

        ActionStep[] steps =
        [
            new(ActionStepType.GameAction, "test/press", Mode: InputMode.Hold, HoldMs: 1500),
            ActionStep.Wait(3000),
        ];

        Stopwatch clock = Stopwatch.StartNew();
        await runner.RunAsync(steps, SequenceOptions.Instant);
        clock.Stop();

        Assert.True(
            clock.Elapsed.TotalMilliseconds < 250,
            $"séquence de 4,5 s simulée en {clock.Elapsed.TotalMilliseconds:F0} ms — attendu : quasi instantané");
    }
}
