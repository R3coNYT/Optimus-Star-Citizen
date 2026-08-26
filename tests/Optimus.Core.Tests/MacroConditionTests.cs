using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Loading;
using Optimus.Core.Personality;

namespace Optimus.Core.Tests;

/// <summary>
/// Les conditions et les répétitions dans les macros.
///
/// Deux choix structurants s'y jouent, et ces essais existent surtout pour eux. D'abord :
/// une condition est tranchée au <b>dépliage</b>, pas à l'exécution — c'est D38, le garde doit
/// voir la séquence complète avant qu'une touche ne parte. Ensuite : le dépliage <b>projette</b>
/// l'effet des pas déjà planifiés, sans quoi un « si » placé après un changement de mode lirait
/// l'état d'avant la macro.
/// </summary>
public sealed class MacroConditionTests
{
    private const string BoundAction = "v_lights_on";
    private const string UnboundAction = "v_quantum_drive";

    private static BindingProfile Bindings() => new(
        "essai", "Profil d'essai", "4.9", "essai",
        [new Binding(BoundAction, InputSpec.Simple("L"))],
        [UnboundAction]);

    /// <summary>
    /// Catalogue minimal : une commande jouable, une qui ne l'est pas, et le mode de vol.
    /// </summary>
    private static CommandCatalog Catalog(params CommandDefinition[] macros) => new(
        "essai", "Catalogue d'essai",
        [
            new CommandDefinition(
                "lights", CommandKind.Action, "Feux", "vaisseau",
                ["les feux"], [ActionStep.Game(BoundAction)])
            {
                PhrasesOn = ["allume les feux"],
                PhrasesOff = ["éteins les feux"],
                ActionsOn = [ActionStep.Game(BoundAction)],
            },
            new CommandDefinition(
                "quantum", CommandKind.Action, "Quantique", "vol",
                ["quantique"], [ActionStep.Game(UnboundAction)]),
            new CommandDefinition(
                MasterMode.CommandId, CommandKind.Action, "Mode de vol", "vol",
                ["mode de vol"], [ActionStep.Game(BoundAction)]),
            .. macros,
        ]);

    private static CommandDefinition Macro(params ActionStep[] steps) =>
        new("macro", CommandKind.Macro, "Séquence", "macros", ["séquence"], steps);

    private static MacroExpansion Plan(CommandDefinition macro, MacroFacts? facts = null)
    {
        CommandCatalog catalog = Catalog(macro);
        return MacroExpander.Plan(macro, catalog, Bindings(), facts: facts);
    }

    // ------------------------------------------------------------------ si, sur ce qui est certain

    [Fact]
    public void Une_commande_jouable_fait_prendre_la_branche_vraie()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.When(
            MacroCondition.Playable("lights"),
            [ActionStep.Call("lights", CommandPolarity.On)],
            [ActionStep.Wait(500)])));

        Assert.Single(plan.Steps);
        Assert.Equal(BoundAction, plan.Steps[0].ActionId);
    }

    /// <summary>
    /// Le cas qui justifie la fonction.
    ///
    /// Sans <c>si</c>, une macro touchant une commande sans raccourci est refusée <b>entière</b>
    /// par le garde : le pilote perd les huit pas qui marchaient pour un seul qui manquait. Avec,
    /// elle contourne ce qu'il n'a pas configuré.
    /// </summary>
    [Fact]
    public void Une_commande_sans_touche_fait_prendre_le_sinon()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.When(
            MacroCondition.Playable("quantum"),
            [ActionStep.Call("quantum")],
            [ActionStep.Call("lights", CommandPolarity.On)])));

        Assert.Single(plan.Steps);
        Assert.Equal(BoundAction, plan.Steps[0].ActionId);
    }

    [Fact]
    public void La_negation_inverse_le_verdict()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.When(
            MacroCondition.Playable("quantum", negated: true),
            [ActionStep.Call("lights", CommandPolarity.On)])));

        Assert.Single(plan.Steps);
    }

    /// <summary>Une branche non retenue disparaît du plan, mais pas de la trace.</summary>
    [Fact]
    public void La_branche_tranchee_est_dite_et_n_est_pas_un_refus()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.When(
            MacroCondition.Playable("quantum"),
            [ActionStep.Call("quantum")])));

        Assert.Empty(plan.Steps);
        Assert.Empty(plan.Skipped);
        Assert.Single(plan.Decisions);
        Assert.Contains("non", plan.Decisions[0], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------- la projection

    /// <summary>
    /// Le piège que la projection évite.
    ///
    /// La macro passe en mode combat, puis demande « si le mode est SCM ». Sans projection, le
    /// dépliage lirait l'état d'<i>avant</i> la macro — NAV — et prendrait la mauvaise branche,
    /// pour une raison que rien dans le fichier ne laisserait deviner.
    /// </summary>
    [Fact]
    public void Un_si_lit_l_effet_des_pas_qui_le_precedent()
    {
        MacroExpansion plan = Plan(Macro(
            ActionStep.Call(MasterMode.CommandId, CommandPolarity.On),
            ActionStep.When(
                MacroCondition.Mode("scm"),
                [ActionStep.Call("lights", CommandPolarity.On)])));

        Assert.Equal(2, plan.Steps.Count);
    }

    /// <summary>Planifier ne doit rien changer à ce qu'Optimus croit réellement.</summary>
    [Fact]
    public void Le_depliage_ne_modifie_pas_les_faits_qu_on_lui_donne()
    {
        MacroFacts facts = new();

        Plan(
            Macro(ActionStep.Call(MasterMode.CommandId, CommandPolarity.On)),
            facts);

        Assert.False(facts.CombatActive);
    }

    /// <summary>
    /// Une bascule au sens neutre efface la croyance au lieu de l'inverser.
    ///
    /// Inverser un état qu'on ignore donnerait un état qu'on ignore tout autant, mais avec
    /// l'assurance en plus.
    /// </summary>
    [Fact]
    public void Une_bascule_de_sens_inconnu_efface_ce_qu_on_croyait_savoir()
    {
        MacroFacts facts = new();
        facts.Note("lights", CommandPolarity.On);
        Assert.True(facts.Believed("lights"));

        facts.Note("lights", CommandPolarity.Neutral);
        Assert.Null(facts.Believed("lights"));
    }

    /// <summary>Sans croyance, la condition est fausse — jamais vraie par défaut.</summary>
    [Fact]
    public void Un_etat_inconnu_ne_declenche_rien()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.When(
            MacroCondition.State("lights", "on"),
            [ActionStep.Call("lights", CommandPolarity.On)])));

        Assert.Empty(plan.Steps);
    }

    // --------------------------------------------------------------------------- répétitions

    [Fact]
    public void Un_bloc_repete_est_deplie_autant_de_fois()
    {
        MacroExpansion plan = Plan(Macro(ActionStep.Loop(
            3, [ActionStep.Call("lights", CommandPolarity.On)])));

        Assert.Equal(3, plan.Steps.Count);
        Assert.All(plan.Steps, step => Assert.Equal(BoundAction, step.ActionId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MacroExpander.MaxRepeat + 1)]
    public void Un_compte_hors_bornes_est_refuse_et_non_tronque(int times)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Plan(Macro(ActionStep.Loop(times, [ActionStep.Call("lights")]))));

        Assert.Contains(MacroExpander.MaxRepeat.ToString(), error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Des répétitions imbriquées se multiplient sans qu'aucune borne individuelle ne cède.
    ///
    /// Trois boucles de vingt font huit mille pas : le plafond global attrape ce que la borne
    /// par boucle ne peut pas voir.
    /// </summary>
    [Fact]
    public void Des_repetitions_imbriquees_butent_sur_le_plafond_global()
    {
        ActionStep innermost = ActionStep.Loop(20, [ActionStep.Call("lights")]);
        ActionStep middle = ActionStep.Loop(20, [innermost]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Plan(Macro(ActionStep.Loop(20, [middle]))));

        Assert.Contains(MacroExpander.MaxSteps.ToString(), error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deux macros qui s'interrogent l'une l'autre ne doivent pas faire tomber le processus.
    ///
    /// Le compteur de renvois du déplieur ne voit rien de ce cas : répondre à « est-elle
    /// jouable ? » ouvre un dépliage <b>neuf</b>, avec son propre compteur. Sans garde
    /// dédiée, c'est un débordement de pile — la seule panne qui tue Optimus sans laisser
    /// de rapport derrière elle.
    /// </summary>
    [Fact]
    public void Deux_conditions_qui_s_interrogent_l_une_l_autre_ne_font_rien_tomber()
    {
        CommandDefinition first = new(
            "macro.a", CommandKind.Macro, "A", "macros", ["a"],
            [ActionStep.When(MacroCondition.Playable("macro.b"), [ActionStep.Call("lights")])]);

        CommandDefinition second = new(
            "macro.b", CommandKind.Macro, "B", "macros", ["b"],
            [ActionStep.When(MacroCondition.Playable("macro.a"), [ActionStep.Call("lights")])]);

        CommandCatalog catalog = Catalog(first, second);

        MacroExpansion plan = MacroExpander.Plan(first, catalog, Bindings());

        Assert.Single(plan.Decisions);
    }

    // ------------------------------------------------------------------------- aller-retour

    /// <summary>
    /// Ce qu'on écrit doit être exactement ce qu'on relit.
    ///
    /// Lecteur et écrivain sont deux traductions du même format, écrites à deux endroits : rien
    /// n'empêche l'un d'oublier ce que l'autre pose. Un aller-retour sur disque est le seul
    /// contrôle qui les tienne ensemble — et l'oubli se paierait par une macro qui perd sa
    /// condition en silence au prochain démarrage.
    /// </summary>
    [Fact]
    public void Une_macro_conditionnelle_survit_a_l_ecriture_et_a_la_relecture()
    {
        CommandDefinition macro = Macro(
            ActionStep.When(
                MacroCondition.Guaranteed("lights", CommandPolarity.Off, negated: true),
                [ActionStep.Loop(2, [ActionStep.Call("lights", CommandPolarity.On)])],
                [ActionStep.Wait(250)]),
            ActionStep.When(
                MacroCondition.Mode("scm"),
                [ActionStep.Call("lights", CommandPolarity.Off, requireDirected: true)]));

        string path = Path.Combine(Path.GetTempPath(), $"optimus-essai-{Guid.NewGuid():N}.json");

        try
        {
            UserMacros.Save(path, [macro]);
            CommandDefinition relu = UserMacros.Load(path).Value.Commands.Single();

            Assert.Equal(2, relu.Actions.Count);

            ActionStep first = relu.Actions[0];
            Assert.Equal(ActionStepType.If, first.Type);
            Assert.Equal(ConditionSubject.Directed, first.Condition!.Subject);
            Assert.Equal(CommandPolarity.Off, first.Condition.Polarity);
            Assert.True(first.Condition.Negated);
            Assert.Equal(ActionStepType.Repeat, first.Block.Single().Type);
            Assert.Equal(2, first.Block.Single().Repeat);
            Assert.Equal(250, first.Alternative.Single().WaitMs);

            ActionStep second = relu.Actions[1];
            Assert.Equal("scm", second.Condition!.Value);
            Assert.True(second.Block.Single().RequireDirected);
            Assert.Empty(second.Alternative);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------------------------------------------------------------------------- validation

    /// <summary>
    /// Le contrôle inspecte les deux branches, là où le dépliage n'en retient qu'une.
    ///
    /// C'est le point important : une branche que la configuration actuelle ne prend jamais sera
    /// prise le jour où le pilote assignera la touche manquante. Un renvoi cassé qui s'y cache
    /// empêcherait alors le catalogue <b>entier</b> de se charger, et Optimus démarrerait muet.
    /// </summary>
    [Fact]
    public void Le_controle_voit_un_renvoi_casse_dans_la_branche_non_prise()
    {
        CommandDefinition macro = Macro(ActionStep.When(
            MacroCondition.Playable("lights"),
            [ActionStep.Call("lights", CommandPolarity.On)],
            [ActionStep.Call("commande.qui.n.existe.pas")]));

        MacroValidator.Verdict verdict = MacroValidator.Check(macro, Catalog(), Bindings());

        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Errors, e => e.Contains("n.existe.pas", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_condition_sur_une_commande_inconnue_est_refusee()
    {
        CommandDefinition macro = Macro(ActionStep.When(
            MacroCondition.Playable("fantome"),
            [ActionStep.Call("lights")]));

        MacroValidator.Verdict verdict = MacroValidator.Check(macro, Catalog(), Bindings());

        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void Un_mode_de_vol_qui_n_existe_pas_est_refuse()
    {
        CommandDefinition macro = Macro(ActionStep.When(
            MacroCondition.Mode("hyperespace"),
            [ActionStep.Call("lights")]));

        MacroValidator.Verdict verdict = MacroValidator.Check(macro, Catalog(), Bindings());

        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Errors, e => e.Contains("nav", StringComparison.Ordinal));
    }

    /// <summary>
    /// Se fier à un état supposé mérite d'être dit, sans être interdit.
    ///
    /// Optimus ne connaît que les commutations qu'il a lui-même provoquées : le pilote qui
    /// écrit cette macro doit le savoir en l'écrivant, pas en la voyant se tromper.
    /// </summary>
    [Fact]
    public void Se_fier_a_un_etat_suppose_vaut_un_avertissement()
    {
        CommandDefinition macro = Macro(ActionStep.When(
            MacroCondition.State("lights", "on"),
            [ActionStep.Call("lights", CommandPolarity.Off)]));

        MacroValidator.Verdict verdict = MacroValidator.Check(macro, Catalog(), Bindings());

        Assert.True(verdict.IsValid);
        Assert.Contains(verdict.Warnings, w => w.Contains("supposé", StringComparison.Ordinal));
    }
}
