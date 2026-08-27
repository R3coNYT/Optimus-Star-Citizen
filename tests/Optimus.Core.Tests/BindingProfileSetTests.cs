using Optimus.Core.Bindings;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Tests;

/// <summary>
/// Les profils de touches.
///
/// Un profil <b>est</b> un jeu d'assignations nommé : le fichier que l'overlay écrivait déjà.
/// Ce qui mérite d'être éprouvé n'est donc pas le stockage — il ne change pas — mais les gestes
/// qui l'entourent, et surtout ceux qui pourraient faire perdre du travail : renommer, supprimer,
/// résoudre un profil qui n'existe plus.
/// </summary>
public sealed class BindingProfileSetTests : IDisposable
{
    private readonly string _root;

    public BindingProfileSetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"optimus-profils-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Un dossier temporaire qui survit n'est pas un echec d'essai.
        }
    }

    private void Seed(string name, params string[] actions)
    {
        BindingOverlay overlay = new();

        foreach (string action in actions)
        {
            overlay.Assign(action, InputSpec.Simple("B"), AssignmentOrigin.Manual);
        }

        overlay.Save(BindingProfileSet.PathOf(name, _root));
    }

    // ------------------------------------------------------------------------ l'inventaire

    [Fact]
    public void Un_dossier_vide_ne_contient_aucun_profil()
    {
        Assert.Empty(BindingProfileSet.List(_root));
    }

    [Fact]
    public void Les_profils_sont_listes_avec_leur_nombre_d_assignations()
    {
        Seed("Minage", "a", "b");
        Seed("Chasse", "c");

        IReadOnlyList<BindingProfileInfo> profiles = BindingProfileSet.List(_root);

        Assert.Equal(["Chasse", "Minage"], profiles.Select(p => p.Name));
        Assert.Equal(1, profiles[0].Count);
        Assert.Equal(2, profiles[1].Count);
    }

    // -------------------------------------------------------------------------- la resolution

    /// <summary>
    /// Sans profil du tout, il faut quand même un nom.
    ///
    /// Sinon le premier lancement enregistrerait les assignations du pilote dans un fichier
    /// sans nom, c'est-à-dire nulle part.
    /// </summary>
    [Fact]
    public void Sans_aucun_profil_le_nom_par_defaut_est_rendu()
    {
        Assert.Equal(BindingProfileSet.DefaultName, BindingProfileSet.Resolve(null, _root));
    }

    /// <summary>
    /// Le cas qui compte : un profil supprimé à la main hors de l'application.
    ///
    /// Rendre le nom disparu laisserait Optimus composer sur un fichier absent — donc sans
    /// aucune assignation — sans que rien ne l'explique. Retomber sur un profil existant est
    /// visible, et le pilote comprend en une seconde.
    /// </summary>
    [Fact]
    public void Un_profil_enregistre_mais_disparu_retombe_sur_un_existant()
    {
        Seed("Chasse", "a");

        Assert.Equal("Chasse", BindingProfileSet.Resolve("Minage", _root));
    }

    [Fact]
    public void Un_profil_enregistre_et_present_est_conserve()
    {
        Seed("Chasse", "a");
        Seed("Minage", "b");

        Assert.Equal("Minage", BindingProfileSet.Resolve("Minage", _root));
    }

    // ------------------------------------------------------------------------- la creation

    /// <summary>
    /// La duplication est le geste utile : un profil « Minage » part du profil de vol habituel,
    /// dont on ne change ensuite qu'une poignée de touches.
    /// </summary>
    [Fact]
    public void Dupliquer_reprend_les_assignations_de_la_source()
    {
        Seed("Standard", "portes", "lumieres");

        BindingProfileSet.Create("Minage", copyFrom: "Standard", root: _root);

        Assert.Equal(2, BindingOverlay.Load(BindingProfileSet.PathOf("Minage", _root)).Count);
    }

    [Fact]
    public void Creer_sans_source_donne_un_profil_vide()
    {
        Seed("Standard", "portes");

        BindingProfileSet.Create("Minage", root: _root);

        Assert.Equal(0, BindingOverlay.Load(BindingProfileSet.PathOf("Minage", _root)).Count);
    }

    /// <summary>Créer par-dessus un profil existant l'écraserait : c'est refusé.</summary>
    [Fact]
    public void Creer_un_profil_deja_present_est_refuse()
    {
        Seed("Minage", "a");

        Assert.Throws<InvalidOperationException>(
            () => BindingProfileSet.Create("Minage", root: _root));

        Assert.Equal(1, BindingOverlay.Load(BindingProfileSet.PathOf("Minage", _root)).Count);
    }

    // -------------------------------------------------------------- renommage et suppression

    [Fact]
    public void Renommer_deplace_les_assignations()
    {
        Seed("Minage", "a", "b");

        BindingProfileSet.Rename("Minage", "Extraction", _root);

        Assert.False(File.Exists(BindingProfileSet.PathOf("Minage", _root)));
        Assert.Equal(2, BindingOverlay.Load(BindingProfileSet.PathOf("Extraction", _root)).Count);
    }

    /// <summary>Renommer vers un nom pris écraserait le travail de l'autre profil.</summary>
    [Fact]
    public void Renommer_vers_un_nom_pris_est_refuse()
    {
        Seed("Minage", "a");
        Seed("Chasse", "b", "c");

        Assert.Throws<InvalidOperationException>(
            () => BindingProfileSet.Rename("Minage", "Chasse", _root));

        Assert.Equal(2, BindingOverlay.Load(BindingProfileSet.PathOf("Chasse", _root)).Count);
    }

    /// <summary>
    /// Le dernier profil ne se supprime pas.
    ///
    /// Sans profil, l'écran des touches deviendrait un formulaire qui n'enregistre rien : le
    /// pilote assignerait, et rien ne serait retenu.
    /// </summary>
    [Fact]
    public void Le_dernier_profil_ne_peut_pas_etre_supprime()
    {
        Seed("Standard", "a");

        Assert.Throws<InvalidOperationException>(
            () => BindingProfileSet.Delete("Standard", _root));

        Assert.True(File.Exists(BindingProfileSet.PathOf("Standard", _root)));
    }

    [Fact]
    public void Un_profil_parmi_d_autres_se_supprime()
    {
        Seed("Standard", "a");
        Seed("Minage", "b");

        BindingProfileSet.Delete("Minage", _root);

        Assert.Single(BindingProfileSet.List(_root));
    }

    // ---------------------------------------------------------------------------- le nommage

    /// <summary>
    /// Un nom se nettoie, il ne se refuse pas.
    ///
    /// « Chasse / Escorte » est parfaitement sensé ; opposer au pilote une règle de nommage de
    /// système de fichiers serait lui faire porter une contrainte technique.
    /// </summary>
    [Theory]
    [InlineData("Chasse / Escorte", "Chasse - Escorte")]
    [InlineData("  Minage  ", "Minage")]
    [InlineData("Cargo:lourd", "Cargo-lourd")]
    public void Un_nom_impossible_est_nettoye(string given, string expected)
    {
        Assert.Equal(expected, BindingProfileSet.Sanitize(given));
    }

    /// <summary>Un nom vide après nettoyage donnerait un fichier « .json », invisible.</summary>
    [Fact]
    public void Un_nom_vide_retombe_sur_le_defaut()
    {
        Assert.Equal(BindingProfileSet.DefaultName, BindingProfileSet.Sanitize("   "));
    }

    // ------------------------------------------------------------------- les commandes vocales

    [Fact]
    public void Chaque_profil_apporte_sa_commande_de_bascule()
    {
        Seed("Minage", "a");
        Seed("Chasse", "b");

        IReadOnlyList<CommandDefinition> commands =
            BindingProfileSet.Commands(BindingProfileSet.List(_root));

        Assert.Equal(2, commands.Count);
        Assert.Contains(commands, c => c.AllPhrases.Contains("profil Minage"));
    }

    /// <summary>
    /// Une commande de bascule n'envoie <b>aucune</b> touche, et c'est essentiel.
    ///
    /// Une commande active serait soumise à la garde, qui exige Star Citizen au premier plan :
    /// changer de profil depuis le bureau deviendrait impossible pour une raison que rien
    /// n'expliquerait.
    /// </summary>
    [Fact]
    public void Une_bascule_de_profil_est_passive()
    {
        Seed("Minage", "a");

        CommandDefinition command = BindingProfileSet
            .Commands(BindingProfileSet.List(_root))
            .Single();

        Assert.True(command.IsPassive);
        Assert.Equal(CommandKind.Query, command.Kind);
    }

    /// <summary>
    /// Le nom voyage dans la description, parce que l'identifiant est normalisé donc
    /// irréversible.
    /// </summary>
    [Fact]
    public void Le_nom_du_profil_se_retrouve_depuis_la_commande()
    {
        Seed("Chasse / Escorte".Replace('/', '-'), "a");

        CommandDefinition command = BindingProfileSet
            .Commands(BindingProfileSet.List(_root))
            .Single();

        Assert.Equal("Chasse - Escorte", BindingProfileSet.ProfileOf(command));
    }

    [Fact]
    public void Une_commande_ordinaire_ne_designe_aucun_profil()
    {
        CommandDefinition other = new(
            "ship.lights.toggle", CommandKind.Action, "Feux", "vaisseau",
            ["les feux"], [ActionStep.Game("v_lights")]);

        Assert.Null(BindingProfileSet.ProfileOf(other));
    }

    /// <summary>
    /// Deux noms qui se normalisent pareil ne doivent produire qu'une commande.
    ///
    /// La grammaire ne garde qu'une correspondance par identifiant : en déclarer deux rendrait
    /// l'une des deux inatteignable, en silence.
    ///
    /// Les deux noms diffèrent par un accent, et c'est délibéré : la casse seule ne suffirait
    /// pas à monter le cas, Windows tenant « Minage.json » et « minage.json » pour un seul et
    /// même fichier. L'essai passerait alors sans jamais exercer la déduplication.
    /// </summary>
    [Fact]
    public void Deux_noms_qui_se_normalisent_pareil_ne_donnent_qu_une_commande()
    {
        Seed("Minage", "a");
        Seed("Minagé", "b");

        Assert.Equal(2, BindingProfileSet.List(_root).Count);
        Assert.Single(BindingProfileSet.Commands(BindingProfileSet.List(_root)));
    }
}
