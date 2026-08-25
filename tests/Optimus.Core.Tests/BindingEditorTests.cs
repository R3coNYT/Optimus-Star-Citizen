using System.Xml.Linq;
using Optimus.Core.Bindings;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// L'editeur de keybinds doit faire l'aller-retour complet : ce qu'Optimus enregistre doit
/// pouvoir etre relu par Star Citizen, sans quoi assigner une touche ne servirait a rien.
/// </summary>
public sealed class BindingEditorTests
{
    [Theory]
    [InlineData("l", "L")]
    [InlineData("kb1_l", "L")]
    [InlineData("space", "SPACE")]
    [InlineData("np_add", "NP_PLUS")]
    [InlineData("mouse1", "MOUSE1")]
    [InlineData("f5", "F5")]
    public void Les_noms_de_touches_du_jeu_se_traduisent(string raw, string expected)
    {
        string cleaned = raw.StartsWith("kb1_", StringComparison.Ordinal) ? raw[4..] : raw;

        InputSpec? spec = ScKeyNames.Parse(cleaned);

        Assert.NotNull(spec);
        Assert.Equal(expected, spec!.Key);
    }

    [Fact]
    public void Une_combinaison_se_relit_telle_qu_elle_a_ete_ecrite()
    {
        InputSpec? parsed = ScKeyNames.Parse("lalt+k");

        Assert.NotNull(parsed);
        Assert.Equal("K", parsed!.Key);
        Assert.Equal(["LALT"], parsed.Modifiers);

        // L'aller-retour est ce qui compte : Optimus doit savoir REECRIRE ce qu'il a lu, sinon
        // il ne peut rien assigner dans un fichier que le jeu accepte.
        Assert.Equal("lalt+k", ScKeyNames.Format(parsed));
    }

    [Fact]
    public void Un_mappage_ecrit_par_Optimus_se_relit_sans_perte()
    {
        LayoutEntry[] entries =
        [
            new("spaceship_general/v_toggle_all_doors", InputSpec.Simple("K")),
            new("lights_controller/v_lights_off", new InputSpec("L", ["LALT"])),
        ];

        XDocument document = ScLayoutXml.Write(entries, "optimus");
        LayoutImport reread = ScLayoutXml.Parse(document);

        Assert.Equal(2, reread.Entries.Count);
        Assert.Empty(reread.Skipped);

        LayoutEntry doors = reread.Entries.Single(e => e.ActionId.EndsWith("v_toggle_all_doors", StringComparison.Ordinal));
        Assert.Equal("K", doors.Input.Key);

        LayoutEntry lights = reread.Entries.Single(e => e.ActionId.EndsWith("v_lights_off", StringComparison.Ordinal));
        Assert.Equal("L", lights.Input.Key);
        Assert.Equal(["LALT"], lights.Input.Modifiers);
    }

    [Fact]
    public void Une_touche_retiree_par_le_pilote_est_signalee_et_non_deduite()
    {
        // Le jeu ecrit une chaine vide pour dire « cette action n'a plus de touche ». La lire
        // comme une absence de donnee ferait garder le defaut, donc l'inverse du choix du pilote.
        XDocument document = XDocument.Parse(
            "<ActionMaps><actionmap name=\"spaceship_general\">" +
            "<action name=\"v_toggle_all_doors\" keyboard=\" \" /></actionmap></ActionMaps>");

        LayoutImport import = ScLayoutXml.Parse(document);

        Assert.Empty(import.Entries);
        Assert.Single(import.Skipped);
        Assert.Contains("retirée", import.Skipped[0]);
    }

    [Fact]
    public void Une_assignation_survit_a_l_ecriture_et_a_la_relecture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"optimus-overlay-{Guid.NewGuid():N}.json");

        try
        {
            BindingOverlay overlay = new();
            overlay.Assign("spaceship_general/v_toggle_all_doors", InputSpec.Simple("K"), AssignmentOrigin.Manual);
            overlay.Assign("lights_controller/v_lights_off", new InputSpec("L", ["LALT"]), AssignmentOrigin.ImportedLayout);
            overlay.Save(path);

            BindingOverlay reloaded = BindingOverlay.Load(path);

            Assert.Equal(2, reloaded.Count);
            Assert.Equal("K", reloaded.Find("spaceship_general/v_toggle_all_doors")!.Input.Key);
            Assert.Equal(AssignmentOrigin.ImportedLayout, reloaded.Find("lights_controller/v_lights_off")!.Origin);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Deux_actions_sur_la_meme_touche_sont_signalees()
    {
        BindingOverlay overlay = new();
        overlay.Assign("a/one", InputSpec.Simple("K"), AssignmentOrigin.Manual);
        overlay.Assign("b/two", InputSpec.Simple("K"), AssignmentOrigin.Manual);

        Assert.Single(overlay.Conflicts());
    }

    [Fact]
    public void Une_action_assignee_cesse_d_etre_sans_touche()
    {
        BindingProfile profile = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(TestData.RepositoryRoot, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;

        const string doors = "spaceship_general/v_toggle_all_doors";
        Assert.Equal(BindingLookup.NotBound, profile.Resolve(doors, out _));

        // C'est tout l'objet de l'editeur : faire passer les portes de « aucun raccourci » a
        // executable, sans toucher au profil du jeu qui doit rester remplacable.
        BindingProfile composed = profile.WithOverrides([new Binding(doors, InputSpec.Simple("K"))]);

        Assert.Equal(BindingLookup.Bound, composed.Resolve(doors, out Binding? binding));
        Assert.Equal("K", binding!.Input.Key);
        Assert.Equal(BindingLookup.NotBound, profile.Resolve(doors, out _));
    }
}
