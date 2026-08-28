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
        const int attempts = 5;

        SimulatedInputEngine engine = new();
        SequenceRunner runner = new(engine, Profile);

        ActionStep[] steps =
        [
            new(ActionStepType.GameAction, "test/press", Mode: InputMode.Hold, HoldMs: requested),
        ];

        double[] measures = new double[attempts];

        for (int i = 0; i < attempts; i++)
        {
            Stopwatch clock = Stopwatch.StartNew();
            await runner.RunAsync(steps, new SequenceOptions(RealTime: true));
            clock.Stop();

            measures[i] = clock.Elapsed.TotalMilliseconds;
        }

        // On retient le PLUS COURT des essais, et non le dernier.
        //
        // Ce que ce test surveille, c'est le dépassement propre à l'implémentation : le premier
        // essai en jeu montrait un maintien de 45 ms qui en durait 96, par granularité du
        // minuteur. Ce dépassement-là est systématique, il se retrouve dans chaque essai, donc
        // dans le plus court. L'ordonnanceur, lui, ne fait qu'ajouter du temps quand la machine
        // est occupée : prendre le minimum l'élimine dès qu'un essai obtient sa part.
        //
        // La distinction n'est pas théorique. Ce test a cédé deux fois à 173 ms, sans qu'aucune
        // ligne du runner ait bougé : l'application tournait à côté. Une borne simplement
        // élargie aurait tenu, mais aurait aussi laissé passer l'ancien comportement, dont le
        // dépassement est du même ordre à cette durée. Le minimum sépare les deux causes.
        double best = measures.Min();

        Assert.True(
            best >= requested - 5 && best <= requested + 40,
            $"maintien de {requested} ms mesuré à {best:F0} ms au mieux "
            + $"(essais : {string.Join(" ms, ", measures.Select(m => m.ToString("F0")))} ms)");
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
