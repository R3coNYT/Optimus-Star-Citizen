using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Loading;
using Optimus.Core.Personality;

namespace Optimus.Core.Tests;

/// <summary>
/// Une macro enchaine plusieurs commandes. Ce qui la rend fiable - ou pas - tient a trois
/// choses : qu'elle vise des SENS et non des bascules, qu'elle soit verifiee AVANT d'agir, et
/// qu'elle ne s'appelle pas elle-meme.
/// </summary>
public sealed class MacroTests
{
    private readonly CommandCatalog _catalog;
    private readonly BindingProfile _defaults;

    public MacroTests()
    {
        string root = TestData.RepositoryRoot;
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(root, "data", "commands", "starcitizen.core.json")).Value;
        _defaults = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(root, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;
    }

    /// <summary>Profil ou tout ce dont les macros ont besoin porte une touche.</summary>
    private BindingProfile FullyBound() => _defaults.WithOverrides([
        new Binding("spaceship_general/v_toggle_all_doors", InputSpec.Simple("P")),
        new Binding("lights_controller/v_lights_on", InputSpec.Simple("F13")),
        new Binding("lights_controller/v_lights_off", InputSpec.Simple("F14")),
        new Binding("spaceship_power/v_power_set_on", InputSpec.Simple("F15")),
        new Binding("spaceship_power/v_power_set_off", InputSpec.Simple("F16")),
        new Binding("spaceship_power/v_power_set_thrusters_on", InputSpec.Simple("F17")),
        new Binding("spaceship_power/v_power_set_thrusters_off", InputSpec.Simple("F18")),
        new Binding("spaceship_power/v_power_set_shields_on", InputSpec.Simple("F19")),
        new Binding("spaceship_power/v_power_set_shields_off", InputSpec.Simple("F20")),
        new Binding("spaceship_power/v_power_set_weapons_on", InputSpec.Simple("F21")),
        new Binding("spaceship_power/v_power_set_weapons_off", InputSpec.Simple("F22")),
        new Binding("spaceship_targeting/v_auto_targeting_enable_short", InputSpec.Simple("F23")),
    ]);

    [Fact]
    public void Une_macro_vise_les_actions_dirigees_quand_elles_ont_une_touche()
    {
        Assert.True(_catalog.TryGet("macro.preflight", out CommandDefinition? macro));

        IReadOnlyList<ActionStep> steps = MacroExpander.Expand(macro!, _catalog, FullyBound());

        string[] actions = steps
            .Where(s => s.Type == ActionStepType.GameAction)
            .Select(s => s.ActionId!)
            .ToArray();

        // Le point crucial : une macro qui enchainerait des bascules serait a pile ou face a
        // chaque pas. Cinq pas, une chance sur trente-deux de faire ce qu'on attend.
        Assert.Contains("spaceship_power/v_power_set_on", actions);
        Assert.Contains("lights_controller/v_lights_on", actions);
        Assert.DoesNotContain("spaceship_power/v_power_toggle", actions);
        Assert.DoesNotContain("lights_controller/v_lights", actions);
    }

    [Fact]
    public void Une_macro_retombe_sur_la_bascule_quand_le_sens_n_a_pas_de_touche()
    {
        Assert.True(_catalog.TryGet("macro.preflight", out CommandDefinition? macro));

        // Profil nu : aucune action dirigee n'a de touche. La macro doit rester executable
        // plutot que de refuser tout net - c'est le repli prevu par ActionsFor.
        BindingProfile bare = _defaults.WithOverrides([
            new Binding("spaceship_general/v_toggle_all_doors", InputSpec.Simple("P")),
        ]);

        string[] actions = MacroExpander.Expand(macro!, _catalog, bare)
            .Where(s => s.Type == ActionStepType.GameAction)
            .Select(s => s.ActionId!)
            .ToArray();

        Assert.Contains("spaceship_power/v_power_toggle", actions);
    }

    [Fact]
    public void Une_macro_qui_s_appelle_elle_meme_est_refusee()
    {
        CommandDefinition boucle = new(
            "macro.boucle", CommandKind.Macro, "Boucle", "macro",
            ["boucle"], [ActionStep.Call("macro.boucle")]);

        CommandCatalog catalog = new("essai", "essai", [boucle]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => MacroExpander.Expand(boucle, catalog, _defaults));

        Assert.Contains("calls itself", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_renvoi_vers_une_commande_inexistante_est_refuse()
    {
        CommandDefinition macro = new(
            "macro.fantome", CommandKind.Macro, "Fantôme", "macro",
            ["fantome"], [ActionStep.Call("commande.qui.n.existe.pas")]);

        CommandCatalog catalog = new("essai", "essai", [macro]);

        Assert.Throws<InvalidOperationException>(
            () => MacroExpander.Expand(macro, catalog, _defaults));
    }

    [Fact]
    public async Task Une_macro_dont_un_pas_manque_de_touche_n_envoie_rien_du_tout()
    {
        SimulatedInputEngine engine = new();

        // Les boucliers directionnels n'ont pas de touche par defaut : le troisieme pas est
        // donc inexecutable, et il ne demande pas de sens garanti - il ne sera pas ecarte.
        CommandDefinition macro = new(
            "macro.essai", CommandKind.Macro, "Essai", "macro", ["essai"],
            [
                ActionStep.Call("ship.lights.toggle"),
                ActionStep.Wait(100),
                ActionStep.Call("shields.raise.front"),
            ]);

        CommandCatalog catalog = new("essai", "essai", _catalog.Commands.Append(macro).ToList());
        CommandExecutor executor = new(catalog, _defaults, engine);

        ExecutionResult result = await executor.ExecuteCommandAsync(
            macro, ExecutionEnvironment.Sandbox, SequenceOptions.Instant);

        // Jouer le premier pas puis s'arreter laisserait le vaisseau dans un etat intermediaire
        // que personne n'a demande. La verification precede l'action.
        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Empty(engine.Events);
        Assert.Contains("v_shield_raise_level_forward", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_pas_qui_exige_un_sens_garanti_est_ecarte_plutot_que_bascule()
    {
        Assert.True(_catalog.TryGet("macro.preflight", out CommandDefinition? macro));

        // Le jeu n'expose aucun sens pour les portes. Basculer les aurait OUVERTES une fois sur
        // deux au moment de decoller - constate en vol le 2026-08-26.
        MacroExpansion plan = MacroExpander.Plan(macro!, _catalog, FullyBound());

        Assert.DoesNotContain(plan.Steps, step =>
            step.ActionId == "spaceship_general/v_toggle_all_doors");
        Assert.Contains(plan.Skipped, reason => reason.Contains("Portes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_macro_qui_s_annonce_ne_recoit_pas_de_replique_en_plus()
    {
        SimulatedInputEngine engine = new();
        CommandExecutor executor = new(_catalog, FullyBound(), engine);
        Assert.True(_catalog.TryGet("macro.preflight", out CommandDefinition? macro));

        ExecutionResult result = await executor.ExecuteCommandAsync(
            macro!, ExecutionEnvironment.Sandbox, SequenceOptions.Instant);

        Assert.Equal(ExecutionStatus.Simulated, result.Status);
        Assert.True(result.Narrated, "la sequence contient des etapes parlees");

        // Entendre « Vaisseau paré » puis « Conforme » sonnerait comme deux copilotes.
        Assert.Null(ResponseRouter.Route(result));
    }

    [Fact]
    public void Toutes_les_macros_du_catalogue_se_deplient()
    {
        BindingProfile bound = FullyBound();

        foreach (CommandDefinition macro in _catalog.Commands.Where(c => c.Kind == CommandKind.Macro))
        {
            IReadOnlyList<ActionStep> steps = MacroExpander.Expand(macro, _catalog, bound);

            Assert.NotEmpty(steps);
            Assert.DoesNotContain(steps, s => s.Type == ActionStepType.Command);
        }
    }
}
