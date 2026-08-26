using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>Horloge pilotee, pour franchir les temporisations sans attendre.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}


/// <summary>
/// « Éteins » doit vouloir dire éteindre, pas inverser. Le jeu ne rendant compte de rien, c'est
/// tout ce qui sépare la commande dirigée d'un simple synonyme.
/// </summary>
public sealed class PolarityTests
{
    private readonly CommandCatalog _catalog;
    private readonly BindingProfile _bindings;

    public PolarityTests()
    {
        string root = TestData.RepositoryRoot;
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(root, "data", "commands", "starcitizen.core.json")).Value;
        _bindings = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(root, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;
    }

    [Theory]
    [InlineData("Optimus, allume les lumieres", "ship.lights.toggle", CommandPolarity.On)]
    [InlineData("Optimus, eteins les lumieres", "ship.lights.toggle", CommandPolarity.Off)]
    [InlineData("Optimus, active le scan", "scan.mode.toggle", CommandPolarity.On)]
    [InlineData("Optimus, desactive le scan", "scan.mode.toggle", CommandPolarity.Off)]
    [InlineData("Optimus, sors le train", "flight.landing_gear.toggle", CommandPolarity.On)]
    [InlineData("Optimus, rentre le train", "flight.landing_gear.toggle", CommandPolarity.Off)]
    [InlineData("Optimus, lumieres", "ship.lights.toggle", CommandPolarity.Neutral)]
    public void Le_sens_demande_survit_a_la_reconnaissance(
        string utterance, string commandId, CommandPolarity expected)
    {
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        Assert.Equal(commandId, resolution.Best!.Command.Id);
        Assert.Equal(expected, resolution.Best.Polarity);
        Assert.Equal(IntentDecision.Execute, resolution.Decision);
    }

    [Fact]
    public void Une_phrase_ne_peut_pas_demander_les_deux_sens_a_la_fois()
    {
        foreach (CommandDefinition command in _catalog.Commands)
        {
            // Une phrase presente dans les deux listes serait indexee deux fois, et la version
            // neutre l'emporterait : le sens serait perdu sans que rien ne le signale.
            Assert.Empty(command.PhrasesOn.Intersect(command.PhrasesOff, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(command.VoicePhrases.Intersect(command.PhrasesOn, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(command.VoicePhrases.Intersect(command.PhrasesOff, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Eteindre_ce_qui_est_deja_eteint_n_envoie_rien()
    {
        SimulatedInputEngine engine = new();
        ManualClock clock = new();
        CommandExecutor executor = new(_catalog, _bindings, engine, guard: new ExecutionGuard(clock));
        Assert.True(_catalog.TryGet("scan.mode.toggle", out CommandDefinition? scan));

        // On allume, donc Optimus sait que c'est allume.
        ExecutionResult on = await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant,
            polarity: CommandPolarity.On);
        Assert.Equal(ExecutionStatus.Simulated, on.Status);

        // On eteint : la bascule agit.
        clock.Advance(TimeSpan.FromSeconds(5));
        engine.Reset();
        ExecutionResult off = await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant,
            polarity: CommandPolarity.Off);
        Assert.Equal(ExecutionStatus.Simulated, off.Status);

        // On redemande l'extinction. Une bascule aveugle rallumerait ; ici rien ne part.
        clock.Advance(TimeSpan.FromSeconds(5));
        engine.Reset();
        ExecutionResult again = await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant,
            polarity: CommandPolarity.Off);

        Assert.Equal(ExecutionStatus.NoChangeNeeded, again.Status);
        Assert.Empty(engine.Events);
        Assert.True(again.Succeeded, "ne rien avoir a faire n'est pas un echec");
    }

    [Fact]
    public async Task Insister_passe_outre_une_croyance_erronee()
    {
        SimulatedInputEngine engine = new();
        ManualClock clock = new();
        CommandExecutor executor = new(_catalog, _bindings, engine, guard: new ExecutionGuard(clock));
        Assert.True(_catalog.TryGet("scan.mode.toggle", out CommandDefinition? scan));

        await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant, polarity: CommandPolarity.Off);

        clock.Advance(TimeSpan.FromSeconds(5));
        ExecutionResult refused = await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant, polarity: CommandPolarity.Off);
        Assert.Equal(ExecutionStatus.NoChangeNeeded, refused.Status);

        // Le pilote repete : c'est que la croyance etait fausse. Sans cette porte de sortie,
        // une memoire desynchronisee bloquerait la commande pour de bon.
        clock.Advance(TimeSpan.FromSeconds(5));
        engine.Reset();
        ExecutionResult forced = await executor.ExecuteCommandAsync(
            scan!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant, polarity: CommandPolarity.Off);

        Assert.Equal(ExecutionStatus.Simulated, forced.Status);
        Assert.NotEmpty(engine.Events);
    }

    [Fact]
    public void Une_action_dirigee_sans_touche_laisse_la_place_a_la_bascule()
    {
        Assert.True(_catalog.TryGet("ship.lights.toggle", out CommandDefinition? lights));

        // Le jeu declare v_lights_off mais ne lui assigne aucune touche : exiger cette action
        // reviendrait a refuser « eteins les lumieres », alors que la bascule fait l'affaire.
        Assert.NotEmpty(lights!.ActionsOff);
        Assert.Same(lights.Actions, lights.ActionsFor(CommandPolarity.Off, _bindings));
    }

    [Theory]
    [InlineData("Optimus, boucliers", "ship.shields.toggle")]
    [InlineData("Optimus, reduis les boucliers", "power.shields.decrease")]
    [InlineData("Optimus, boucliers a babord", "shields.raise.left")]
    [InlineData("Optimus, equilibre les boucliers", "shields.reset")]
    [InlineData("Optimus, armes", "ship.weapons.toggle")]
    [InlineData("Optimus, priorite a l armement", "power.weapons.increase")]
    [InlineData("Optimus, moteurs", "ship.engines.toggle")]
    [InlineData("Optimus, priorite aux moteurs", "power.engines.increase")]
    public void Un_nom_seul_ne_capture_pas_les_phrases_qui_le_contiennent(
        string utterance, string expectedCommandId)
    {
        // « boucliers » suffit a demander la bascule, mais figure aussi dans une douzaine
        // d'autres formulations. Le validateur signale ces imbrications et s'en remet au score ;
        // ce test verifie que le score fait bien le travail, au lieu de le supposer.
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        Assert.Equal(expectedCommandId, resolution.Best!.Command.Id);
        Assert.Equal(IntentDecision.Execute, resolution.Decision);
    }

    [Theory]
    [InlineData("Optimus, priorite aux armes")]
    [InlineData("Optimus, surveille les boucliers du convoi")]
    public void Un_nom_noye_dans_une_phrase_inconnue_ne_declenche_rien(string utterance)
    {
        // Le score de containment avait un plancher a 0,90 - au-dessus du seuil d'execution -
        // si bien qu'un mot isole revendiquait toute phrase le contenant : « priorite aux
        // armes » basculait les armes a 0,93. La couverture gouverne desormais le score, et
        // ces enonces tombent dans la bande de proposition.
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        Assert.NotEqual(IntentDecision.Execute, resolution.Decision);
    }

    [Theory]
    [InlineData(CommandPolarity.On, "ship.lights.on")]
    [InlineData(CommandPolarity.Off, "ship.lights.off")]
    [InlineData(CommandPolarity.Neutral, "ship.lights.toggle")]
    public void La_replique_suit_le_sens_demande(CommandPolarity polarity, string expectedKey)
    {
        Assert.True(_catalog.TryGet("ship.lights.toggle", out CommandDefinition? lights));

        ExecutionResult result = new(
            TraceId: "test",
            Status: ExecutionStatus.Simulated,
            Intent: null,
            Command: lights,
            Guard: null,
            Steps: [],
            TotalMs: 0,
            Message: null,
            Polarity: polarity);

        // « Voila de la lumiere » apres une extinction sonnerait faux.
        Personality.ResponseRequest? request = Personality.ResponseRouter.Route(result);

        Assert.NotNull(request);
        Assert.Equal(expectedKey, request!.Keys[0]);
    }

    [Theory]
    [InlineData("Optimus, ferme les portes", "spaceship_general/v_close_all_doors")]
    [InlineData("Optimus, ouvre les portes", "spaceship_general/v_open_all_doors")]
    [InlineData("Optimus, mode combat", "spaceship_movement/v_master_mode_set_scm")]
    [InlineData("Optimus, mode navigation", "spaceship_movement/v_master_mode_set_nav")]
    public void Les_actions_dirigees_du_jeu_sont_preferees_quand_elles_ont_une_touche(
        string utterance, string expectedAction)
    {
        // Le jeu expose ces quatre actions depuis toujours ; mon inventaire les avait ratees
        // parce qu'il cherchait des suffixes « _on » et « _off ». Sans elles, « ferme les
        // portes » basculait - donc les ouvrait une fois sur deux.
        BindingProfile bound = _bindings.WithOverrides([
            new Binding("spaceship_general/v_open_all_doors", InputSpec.Simple("F13")),
            new Binding("spaceship_general/v_close_all_doors", InputSpec.Simple("F14")),
            new Binding("spaceship_movement/v_master_mode_set_scm", InputSpec.Simple("F15")),
            new Binding("spaceship_movement/v_master_mode_set_nav", InputSpec.Simple("F16")),
        ]);

        FastIntentMatcher matcher = new(_catalog);
        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        Assert.Equal(IntentDecision.Execute, resolution.Decision);
        Assert.NotEqual(CommandPolarity.Neutral, resolution.Best!.Polarity);

        string[] actions = resolution.Best.Command
            .ActionsFor(resolution.Best.Polarity, bound)
            .Where(s => s.Type == ActionStepType.GameAction)
            .Select(s => s.ActionId!)
            .ToArray();

        Assert.Equal([expectedAction], actions);
    }

    [Fact]
    public void Verrouiller_une_porte_n_est_pas_la_fermer()
    {
        // Le jeu distingue v_close_all_doors de v_lock_all_doors. Mes formulations rangeaient
        // « verrouille les portes » sous « fermer », ce qui melangeait deux gestes distincts.
        Assert.True(_catalog.TryGet("ship.doors.toggle", out CommandDefinition? doors));
        Assert.True(_catalog.TryGet("ship.doorlocks.toggle", out CommandDefinition? locks));

        Assert.DoesNotContain("verrouille les portes", doors!.AllPhrases);
        Assert.Contains("verrouille les portes", locks!.AllPhrases);
    }
}
