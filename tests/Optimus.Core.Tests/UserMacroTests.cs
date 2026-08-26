using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Les macros du pilote vivent hors du catalogue livre - celui-ci est remplace a chaque
/// publication. Et rien ne s'ecrit sans avoir ete verifie : une macro incoherente sur disque
/// empecherait le catalogue entier de se charger au demarrage suivant.
/// </summary>
public sealed class UserMacroTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly CommandCatalog _catalog;
    private readonly BindingProfile _bindings;

    public UserMacroTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"optimus-macros-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "starcitizen.json");

        string root = TestData.RepositoryRoot;
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(root, "data", "commands", "starcitizen.core.json")).Value;
        _bindings = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(root, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;
    }

    private static CommandDefinition Macro(
        string id = "macro.perso.essai",
        string? name = "Mon essai",
        IReadOnlyList<string>? phrases = null,
        IReadOnlyList<ActionStep>? steps = null) =>
        new(id, CommandKind.Macro, name ?? string.Empty, "macro",
            phrases ?? ["mon essai a moi"],
            steps ?? [ActionStep.Call("ship.lights.toggle", CommandPolarity.On), ActionStep.Wait(300)],
            CooldownMs: 3000);

    [Fact]
    public void Une_macro_ecrite_se_relit_a_l_identique()
    {
        CommandDefinition macro = Macro(steps: [
            new ActionStep(ActionStepType.Say, ResponseKey: "system.success"),
            ActionStep.Call("ship.lights.toggle", CommandPolarity.Off, requireDirected: true),
            ActionStep.Wait(450),
        ]);

        UserMacros.Save(_path, [macro]);

        LoadResult<CommandCatalog> reloaded = UserMacros.Load(_path);

        Assert.Empty(reloaded.Issues);
        Assert.True(reloaded.Value.TryGet(macro.Id, out CommandDefinition? read));
        Assert.Equal(macro.Name, read!.Name);
        Assert.Equal(CommandKind.Macro, read.Kind);
        Assert.Equal(3000, read.CooldownMs);
        Assert.Equal(3, read.Actions.Count);

        Assert.Equal(ActionStepType.Say, read.Actions[0].Type);
        Assert.Equal("system.success", read.Actions[0].ResponseKey);

        Assert.Equal(ActionStepType.Command, read.Actions[1].Type);
        Assert.Equal(CommandPolarity.Off, read.Actions[1].Polarity);
        Assert.True(read.Actions[1].RequireDirected);

        Assert.Equal(450, read.Actions[2].WaitMs);
    }

    [Fact]
    public void Une_macro_du_pilote_remplace_celle_qui_porte_le_meme_identifiant()
    {
        CommandDefinition mine = Macro(
            id: "macro.preflight", name: "Mon décollage à moi", phrases: ["mon decollage a moi"]);

        UserMacros.Save(_path, [mine]);

        CommandCatalog merged = CommandCatalog.Merge(
            "fusion", "fusion", _catalog, UserMacros.Load(_path).Value);

        Assert.True(merged.TryGet("macro.preflight", out CommandDefinition? winner));
        Assert.Equal("Mon décollage à moi", winner!.Name);

        // Le catalogue livre reste intact : c'est lui qui permet de revenir en arriere.
        Assert.True(_catalog.TryGet("macro.preflight", out CommandDefinition? original));
        Assert.Equal("Préparation au décollage", original!.Name);
    }

    [Fact]
    public void Le_fichier_absent_donne_un_catalogue_vide_et_non_une_erreur()
    {
        LoadResult<CommandCatalog> loaded = UserMacros.Load(
            Path.Combine(_directory, "rien-du-tout.json"));

        Assert.Empty(loaded.Issues);
        Assert.Equal(0, loaded.Value.Count);
    }

    [Theory]
    [InlineData("ouvre les portes", "formulation deja prise")]
    public void Une_formulation_deja_prise_est_refusee(string phrase, string _)
    {
        MacroValidator.Verdict verdict = MacroValidator.Check(
            Macro(phrases: [phrase]), _catalog, _bindings);

        // Deux commandes ne peuvent pas repondre au meme enonce : la grammaire ne garde qu'une
        // correspondance, l'une des deux deviendrait inatteignable en silence.
        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Errors, e => e.Contains("déjà employée", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_macro_sans_formulation_ni_etape_est_refusee()
    {
        MacroValidator.Verdict verdict = MacroValidator.Check(
            Macro(phrases: [], steps: []), _catalog, _bindings);

        Assert.False(verdict.IsValid);
        Assert.Equal(2, verdict.Errors.Count);
    }

    [Fact]
    public void Un_renvoi_vers_une_commande_inconnue_est_refuse()
    {
        MacroValidator.Verdict verdict = MacroValidator.Check(
            Macro(steps: [ActionStep.Call("commande.imaginaire")]), _catalog, _bindings);

        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Errors, e => e.Contains("commande.imaginaire", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_macro_qui_s_appelle_elle_meme_est_refusee()
    {
        MacroValidator.Verdict verdict = MacroValidator.Check(
            Macro(steps: [ActionStep.Call("macro.perso.essai")]), _catalog, _bindings);

        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void Un_pas_sans_touche_avertit_sans_empecher_d_enregistrer()
    {
        // Le pilote peut assigner la touche apres coup : ce n'est pas une raison de lui refuser
        // l'enregistrement, mais il doit le savoir maintenant plutot qu'au decollage.
        MacroValidator.Verdict verdict = MacroValidator.Check(
            Macro(steps: [ActionStep.Call("shields.raise.front")]), _catalog, _bindings);

        Assert.True(verdict.IsValid);
        Assert.Contains(verdict.Warnings, w => w.Contains("pas de touche", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
