using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Tests de bout en bout du moteur, sur les <b>vraies</b> données du projet : le catalogue de
/// commandes et le profil de binding extrait de Star Citizen 4.9.
///
/// Ils tournent sans micro, sans clavier et sans le jeu — c'est précisément ce que le mode
/// simulation rend possible, et ce qui permettra à la CI de détecter une régression avant
/// qu'elle n'atteigne un cockpit.
/// </summary>
public sealed class PipelineTests
{
    private readonly CommandCatalog _catalog;
    private readonly BindingProfile _bindings;

    public PipelineTests()
    {
        string root = TestData.RepositoryRoot;
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(root, "data", "commands", "starcitizen.core.json")).Value;
        _bindings = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(root, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;
    }

    [Fact]
    public void Catalogue_et_profil_se_chargent_sans_anomalie()
    {
        string root = TestData.RepositoryRoot;

        LoadResult<CommandCatalog> catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(root, "data", "commands", "starcitizen.core.json"));
        LoadResult<BindingProfile> profile = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(root, "data", "bindings", "starcitizen", "defaults-4.9.json"));

        Assert.Empty(catalog.Issues);
        Assert.Empty(profile.Issues);
        Assert.True(catalog.Value.Count >= 50, $"catalogue trop maigre : {catalog.Value.Count}");
        Assert.True(profile.Value.BoundCount >= 600, $"profil trop maigre : {profile.Value.BoundCount}");
    }

    [Fact]
    public void Toute_action_referencee_existe_dans_le_profil()
    {
        // Une action absente du profil signale un catalogue desynchronise du jeu : c'est une
        // erreur, contrairement a une action simplement non assignee.
        List<string> unknown = new();

        foreach (CommandDefinition command in _catalog.Commands)
        {
            foreach (string actionId in command.ReferencedActionIds)
            {
                if (_bindings.Resolve(actionId, out _) == BindingLookup.UnknownAction)
                {
                    unknown.Add($"{command.Id} -> {actionId}");
                }
            }
        }

        Assert.True(unknown.Count == 0, "Actions introuvables : " + string.Join(", ", unknown));
    }

    [Theory]
    [InlineData("Optimus, allume les lumieres", "ship.lights.toggle")]
    [InlineData("optimus sors le train", "flight.landing_gear.toggle")]
    [InlineData("Optimus, lance le quantum", "quantum.engage")]
    [InlineData("ouvre les portes", "ship.doors.toggle")]
    [InlineData("optimus active le scan", "scan.mode.toggle")]
    [InlineData("largue un leurre", "combat.countermeasure.decoy")]
    public void Les_phrases_de_reference_resolvent_la_bonne_commande(string utterance, string expectedId)
    {
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, "Optimus");

        Assert.NotNull(resolution.Best);
        Assert.Equal(expectedId, resolution.Best!.Command.Id);
        Assert.Equal(IntentDecision.Execute, resolution.Decision);
    }

    [Fact]
    public void Une_transcription_fautive_reste_rattrapee()
    {
        // « bouquilles » pour « boucliers » : erreur reellement produite par Whisper lors du
        // spike S0-2. Le matcher doit au minimum proposer la bonne commande.
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve("optimus mets les bouquilles sur l avant", "Optimus");

        Assert.NotNull(resolution.Best);
        Assert.Equal("shields.raise.front", resolution.Best!.Command.Id);
        Assert.NotEqual(IntentDecision.Unknown, resolution.Decision);
    }

    [Theory]
    [InlineData("optimus fais un cafe")]
    [InlineData("quelle heure est-il")]
    [InlineData("blblbl")]
    public void Une_phrase_hors_sujet_est_declaree_incomprise(string utterance)
    {
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, "Optimus");

        Assert.Equal(IntentDecision.Unknown, resolution.Decision);
    }

    [Fact]
    public async Task Une_commande_liee_produit_les_entrees_attendues()
    {
        SimulatedInputEngine engine = new();
        CommandExecutor executor = new(_catalog, _bindings, engine);

        ExecutionResult result = await executor.ExecuteUtteranceAsync(
            "Optimus, allume les lumieres",
            ExecutionEnvironment.Sandbox,
            sequenceOptions: SequenceOptions.Instant);

        Assert.Equal(ExecutionStatus.Simulated, result.Status);
        Assert.Equal("ship.lights.toggle", result.Command!.Id);

        // v_lights est sur la touche L dans le profil par defaut de la 4.9.
        Assert.Collection(
            engine.Events,
            e => AssertEvent(e, InputEventKind.Down, "L"),
            e => AssertEvent(e, InputEventKind.Up, "L"));

        Assert.Empty(engine.StillPressed);
    }

    [Fact]
    public async Task Une_action_sans_raccourci_est_refusee_avec_une_explication()
    {
        // Cas non theorique : v_toggle_all_doors n'a aucune touche par defaut en 4.9.
        SimulatedInputEngine engine = new();
        CommandExecutor executor = new(_catalog, _bindings, engine);

        ExecutionResult result = await executor.ExecuteUtteranceAsync(
            "Optimus, ouvre les portes",
            ExecutionEnvironment.Sandbox,
            sequenceOptions: SequenceOptions.Instant);

        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Equal(GuardVerdict.BindingNotConfigured, result.Guard!.Verdict);
        Assert.Equal("spaceship_general/v_toggle_all_doors", result.Guard.ActionId);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(engine.Events);
    }

    [Fact]
    public async Task Une_commande_dangereuse_exige_une_confirmation()
    {
        SimulatedInputEngine engine = new();
        CommandExecutor executor = new(_catalog, _bindings, engine);
        _catalog.TryGet("ship.self_destruct", out CommandDefinition? selfDestruct);

        ExecutionResult refused = await executor.ExecuteCommandAsync(
            selfDestruct!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant);

        Assert.Equal(ExecutionStatus.Rejected, refused.Status);
        Assert.Equal(GuardVerdict.NeedsConfirmation, refused.Guard!.Verdict);
        Assert.Empty(engine.Events);

        ExecutionResult accepted = await executor.ExecuteCommandAsync(
            selfDestruct!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant, confirmed: true);

        Assert.Equal(ExecutionStatus.Simulated, accepted.Status);
        Assert.NotEmpty(engine.Events);
    }

    [Fact]
    public async Task L_arret_d_urgence_bloque_toute_execution()
    {
        SimulatedInputEngine engine = new();
        CommandExecutor executor = new(_catalog, _bindings, engine);
        ExecutionEnvironment stopped = ExecutionEnvironment.Sandbox with { KillSwitchEngaged = true };

        ExecutionResult result = await executor.ExecuteUtteranceAsync(
            "Optimus, allume les lumieres", stopped, sequenceOptions: SequenceOptions.Instant);

        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Equal(GuardVerdict.KillSwitch, result.Guard!.Verdict);
        Assert.Empty(engine.Events);
    }

    [Fact]
    public async Task Une_sequence_annulee_relache_toutes_les_touches()
    {
        // La garantie la plus importante du moteur : une touche ne doit jamais rester
        // enfoncee, quoi qu'il arrive en cours de sequence.
        SimulatedInputEngine engine = new();
        SequenceRunner runner = new(engine, _bindings);

        ActionStep[] steps =
        {
            new(ActionStepType.GameAction, "lights_controller/v_lights", Mode: InputMode.Press),
            ActionStep.Wait(5_000),
        };

        using CancellationTokenSource cancellation = new();
        Task<IReadOnlyList<SequenceStepTrace>> execution =
            runner.RunAsync(steps, new SequenceOptions(RealTime: true), cancellation.Token);

        await Task.Delay(50);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        Assert.Empty(engine.StillPressed);
        Assert.Contains(engine.Events, e => e.Kind == InputEventKind.Up);
    }

    [Fact]
    public void La_temporisation_empeche_une_double_execution()
    {
        ExecutionGuard guard = new();
        CommandDefinition command = new(
            "test.cooldown", CommandKind.Action, "Test", "system",
            new[] { "test" },
            new[] { ActionStep.Game("lights_controller/v_lights") },
            CooldownMs: 5_000);

        Assert.True(guard.Evaluate(command, _bindings, ExecutionEnvironment.Sandbox).IsAllowed);
        guard.MarkExecuted(command);

        GuardDecision second = guard.Evaluate(command, _bindings, ExecutionEnvironment.Sandbox);
        Assert.Equal(GuardVerdict.CooldownActive, second.Verdict);
    }

    private static void AssertEvent(InputEvent inputEvent, InputEventKind kind, string key)
    {
        Assert.Equal(kind, inputEvent.Kind);
        Assert.Equal(key, inputEvent.Input.Key);
    }
}
