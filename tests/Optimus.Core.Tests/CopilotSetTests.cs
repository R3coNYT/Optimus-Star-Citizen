using Optimus.Core.Domain.Commands;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Les copilotes multiples.
///
/// Le modèle prévoyait plusieurs copilotes depuis l'origine (§7) : ce qui manquait n'était pas
/// la structure mais de quoi en avoir plusieurs et passer de l'un à l'autre. Ce qui mérite
/// d'être éprouvé, ce sont donc les deux règles qui protègent le travail du pilote : un copilote
/// livré ne se supprime pas, et une copie qui le masquait lui rend sa place.
/// </summary>
public sealed class CopilotSetTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly string _userRoot;

    public CopilotSetTests()
    {
        string root = Path.Combine(Path.GetTempPath(), $"optimus-copilotes-{Guid.NewGuid():N}");

        _dataRoot = Path.Combine(root, "depot");
        _userRoot = Path.Combine(root, "pilote");

        Directory.CreateDirectory(_userRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dataRoot)!, recursive: true);
        }
        catch (IOException)
        {
            // Un dossier temporaire qui survit n'est pas un echec d'essai.
        }
    }

    /// <summary>Écrit un copilote minimal, livré ou appartenant au pilote.</summary>
    private void Seed(string id, string name, bool isUsers)
    {
        string directory = isUsers
            ? Path.Combine(_userRoot, id)
            : Path.Combine(CopilotSet.ShippedDirectory(_dataRoot), id);

        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "copilot.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{name}}",
              "language": "fr-FR",
              "wake_word": "{{name}}",
              "voice": { "provider": "windows-onecore", "voice_id": "Microsoft Paul" },
              "personality_ref": "personality.json",
              "responses_ref": "responses.fr.json"
            }
            """);

        File.WriteAllText(Path.Combine(directory, "personality.json"), """
            { "traits": { "humor": 40, "formality": 80 } }
            """);

        File.WriteAllText(Path.Combine(directory, "responses.fr.json"), """
            { "responses": { "system.success": { "any": [ { "text": "Conforme." } ] } } }
            """);
    }

    // ------------------------------------------------------------------------ l'inventaire

    [Fact]
    public void Sans_aucun_dossier_la_liste_est_vide()
    {
        Assert.Empty(CopilotSet.List(_dataRoot, _userRoot));
    }

    [Fact]
    public void Les_copilotes_livres_et_les_votres_se_voient_ensemble()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("virgil", "Virgil", isUsers: true);

        IReadOnlyList<CopilotInfo> copilots = CopilotSet.List(_dataRoot, _userRoot);

        Assert.Equal(2, copilots.Count);
        Assert.False(copilots.Single(c => c.Id == "optimus").IsUsers);
        Assert.True(copilots.Single(c => c.Id == "virgil").IsUsers);
    }

    /// <summary>
    /// Une copie masque l'original sans le détruire.
    ///
    /// C'est la même règle que pour les macros (D43) : le pilote peut infléchir un copilote
    /// livré sans le perdre, et sans que la publication suivante efface son travail.
    /// </summary>
    [Fact]
    public void Un_copilote_a_vous_masque_celui_qui_est_livre()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("optimus", "Optimus le mien", isUsers: true);

        CopilotInfo copilot = Assert.Single(CopilotSet.List(_dataRoot, _userRoot));

        Assert.Equal("Optimus le mien", copilot.Name);
        Assert.True(copilot.IsUsers);
        Assert.Equal(Path.Combine(_userRoot, "optimus"),
            CopilotSet.DirectoryOf("optimus", _dataRoot, _userRoot));
    }

    [Fact]
    public void Un_dossier_sans_manifeste_est_ignore()
    {
        Directory.CreateDirectory(Path.Combine(_userRoot, "vide"));

        Assert.Empty(CopilotSet.List(_dataRoot, _userRoot));
        Assert.Null(CopilotSet.DirectoryOf("vide", _dataRoot, _userRoot));
    }

    // -------------------------------------------------------------------------- resolution

    /// <summary>
    /// Un copilote favori supprimé ne doit pas laisser Optimus muet.
    ///
    /// Le pilote doit retrouver quelqu'un qui parle, quitte à ce que ce ne soit pas celui qu'il
    /// attendait — un écran silencieux n'explique rien.
    /// </summary>
    [Fact]
    public void Un_favori_disparu_retombe_sur_un_copilote_existant()
    {
        Seed("optimus", "Optimus", isUsers: false);

        Assert.Equal("optimus", CopilotSet.Resolve("virgil", _dataRoot, _userRoot));
    }

    [Fact]
    public void Un_favori_present_est_conserve()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("virgil", "Virgil", isUsers: true);

        Assert.Equal("virgil", CopilotSet.Resolve("virgil", _dataRoot, _userRoot));
    }

    // --------------------------------------------------------------------------- creation

    /// <summary>
    /// Dupliquer copie tout — répliques comprises — et donne son identité à la copie.
    ///
    /// Sans identité propre, deux copilotes répondraient au même mot d'éveil et l'un des deux
    /// serait inatteignable.
    /// </summary>
    [Fact]
    public void Dupliquer_copie_tout_et_donne_une_identite_propre()
    {
        Seed("optimus", "Optimus", isUsers: false);

        CopilotSet.Create("Virgil", "Virgil", "optimus", _dataRoot, _userRoot);

        string created = Path.Combine(_userRoot, "virgil");

        Assert.True(File.Exists(Path.Combine(created, "responses.fr.json")));
        Assert.True(File.Exists(Path.Combine(created, "personality.json")));

        CopilotInfo copilot = CopilotSet.List(_dataRoot, _userRoot).Single(c => c.Id == "virgil");

        Assert.Equal("Virgil", copilot.Name);
        Assert.Equal("Virgil", copilot.WakeWord);
    }

    [Fact]
    public void Dupliquer_sur_un_nom_deja_pris_est_refuse()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("virgil", "Virgil", isUsers: true);

        Assert.Throws<InvalidOperationException>(
            () => CopilotSet.Create("Virgil", "Virgil", "optimus", _dataRoot, _userRoot));
    }

    [Fact]
    public void Dupliquer_depuis_un_copilote_inconnu_est_refuse()
    {
        Assert.Throws<InvalidOperationException>(
            () => CopilotSet.Create("Virgil", "Virgil", "fantome", _dataRoot, _userRoot));
    }

    // ------------------------------------------------------------------------ suppression

    /// <summary>
    /// Un copilote livré ne se supprime pas.
    ///
    /// Il reviendrait à la publication suivante, et prétendre le contraire serait mentir au
    /// pilote sur ce qu'il vient de faire.
    /// </summary>
    [Fact]
    public void Un_copilote_livre_ne_se_supprime_pas()
    {
        Seed("optimus", "Optimus", isUsers: false);

        Assert.Throws<InvalidOperationException>(
            () => CopilotSet.Delete("optimus", _userRoot));

        Assert.Single(CopilotSet.List(_dataRoot, _userRoot));
    }

    /// <summary>Supprimer une copie restitue l'original : c'est ce qui rend l'essai sans risque.</summary>
    [Fact]
    public void Supprimer_une_copie_restitue_le_copilote_livre()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("optimus", "Le mien", isUsers: true);

        CopilotSet.Delete("optimus", _userRoot);

        CopilotInfo restored = Assert.Single(CopilotSet.List(_dataRoot, _userRoot));

        Assert.Equal("Optimus", restored.Name);
        Assert.False(restored.IsUsers);
    }

    // ------------------------------------------------------------------- la bascule a la voix

    /// <summary>
    /// Le copilote actif n'a pas de commande pour se rappeler lui-même.
    ///
    /// « Passe à Optimus » alors qu'Optimus répond déjà n'apporte rien, et occuperait une
    /// formulation de la grammaire pour ne rien faire.
    /// </summary>
    [Fact]
    public void Le_copilote_actif_n_a_pas_de_commande_de_bascule()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("virgil", "Virgil", isUsers: true);

        IReadOnlyList<CommandDefinition> commands =
            CopilotSet.Commands(CopilotSet.List(_dataRoot, _userRoot), "optimus");

        CommandDefinition only = Assert.Single(commands);

        Assert.Equal("Virgil", only.Name);
        Assert.Contains("passe à Virgil", only.AllPhrases);
    }

    [Fact]
    public void Un_seul_copilote_n_engendre_aucune_commande()
    {
        Seed("optimus", "Optimus", isUsers: false);

        Assert.Empty(CopilotSet.Commands(CopilotSet.List(_dataRoot, _userRoot), "optimus"));
    }

    /// <summary>Une bascule n'envoie aucune touche : la garde n'a donc pas à exiger le jeu.</summary>
    [Fact]
    public void Une_bascule_de_copilote_est_passive()
    {
        Seed("optimus", "Optimus", isUsers: false);
        Seed("virgil", "Virgil", isUsers: true);

        CommandDefinition command =
            CopilotSet.Commands(CopilotSet.List(_dataRoot, _userRoot), "optimus").Single();

        Assert.True(command.IsPassive);
        Assert.Equal(CommandKind.Query, command.Kind);
        Assert.Equal("virgil", CopilotSet.TargetOf(command));
    }

    [Theory]
    [InlineData("Virgil", "virgil")]
    [InlineData("  Synthia  ", "synthia")]
    [InlineData("Optimus Combat", "optimus-combat")]
    [InlineData("Éclaireur", "eclaireur")]
    public void Un_nom_devient_un_identifiant_de_dossier(string given, string expected)
    {
        Assert.Equal(expected, CopilotSet.Sanitize(given));
    }
}
